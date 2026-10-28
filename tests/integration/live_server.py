#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-only
# Copyright (c) 2026 Fr4ctalNoise and Project A contributors.
"""
Live-server test: runs the release zip inside the official Jellyfin 12.4 image, with
Jellyfin Enhanced 12.14.0.0 installed from its own catalog, and checks the Spike against
the real server (design doc "How it gets verified", item 1, plus load/injection/database).

  python3 tests/integration/live_server.py --zip dist/project-a_X.zip \
      --endpoints dist/endpoints.json --out results/live [--image jellyfin/jellyfin:12.4]

Needs Docker. Standard library only. Every account, password and API key is created fresh
for this throwaway server and never printed; nothing here touches a real server.
Writes summary.json and a readable report.txt to --out, plus the server log.
"""

import argparse
import hashlib
import io
import json
import os
import pathlib
import secrets
import shutil
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile

JE_MANIFEST = "https://raw.githubusercontent.com/n00bcodr/jellyfin-plugins/main/manifest.json"
JE_VERSION = "12.14.0.0"
PLUGIN_GUID = "0fe4dc69-b52d-40bc-ad8d-ff518bad55a3"
CONTAINER = "projecta-live"
REPO = pathlib.Path(__file__).resolve().parents[2]
CLIENT = 'MediaBrowser Client="ProjectA-CI", Device="ci", DeviceId="ci-{}", Version="1.0"'

results = []


def check(name, ok, detail=""):
    results.append({"check": name, "ok": bool(ok), "detail": detail})
    print(("PASS " if ok else "FAIL ") + name + (f"  ({detail})" if detail else ""), flush=True)
    return ok


class Server:
    def __init__(self, base):
        self.base = base.rstrip("/")

    def call(self, method, path, token=None, body=None, device="anon", raw=False):
        headers = {"Authorization": CLIENT.format(device) + (f', Token="{token}"' if token else "")}
        data = None
        if body is not None:
            data = json.dumps(body).encode()
            headers["Content-Type"] = "application/json"
        req = urllib.request.Request(self.base + path, data=data, method=method, headers=headers)
        try:
            with urllib.request.urlopen(req, timeout=60) as r:
                payload = r.read()
                return r.status, dict(r.headers), payload if raw else _json(payload)
        except urllib.error.HTTPError as e:
            payload = e.read()
            return e.code, dict(e.headers), payload if raw else _json(payload)


def _json(payload):
    try:
        return json.loads(payload) if payload else None
    except ValueError:
        return payload.decode("utf-8", "replace")


def lowered(value):
    """Jellyfin's API uses PascalCase; compare field names case-insensitively."""
    return {k.lower(): v for k, v in value.items()} if isinstance(value, dict) else value


def docker(*args, check_ok=True):
    return subprocess.run(["docker", *args], check=check_ok, capture_output=True, text=True)


def install_jellyfin_enhanced(plugins_dir):
    with urllib.request.urlopen(JE_MANIFEST, timeout=60) as r:
        manifest = json.load(r)
    je = next(p for p in manifest if p.get("name") == "Jellyfin Enhanced")
    version = next(v for v in je["versions"] if v["version"] == JE_VERSION and v["targetAbi"].startswith("12."))
    with urllib.request.urlopen(version["sourceUrl"], timeout=120) as r:
        blob = r.read()
    if hashlib.md5(blob).hexdigest().lower() != version["checksum"].lower():
        sys.exit("Jellyfin Enhanced download checksum mismatch")
    target = plugins_dir / f"Jellyfin Enhanced_{JE_VERSION}"
    target.mkdir(parents=True)
    zipfile.ZipFile(io.BytesIO(blob)).extractall(target)
    return target


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--zip", required=True)
    ap.add_argument("--endpoints", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--image", default="jellyfin/jellyfin:12.4")
    ap.add_argument("--port", type=int, default=8096)
    ap.add_argument("--keep", action="store_true", help="leave the server running (for the UI test)")
    ap.add_argument("--creds", help="where to write the throwaway test user's sign-in for the UI test; must be outside --out (results are published)")
    args = ap.parse_args()
    out = pathlib.Path(args.out)
    out.mkdir(parents=True, exist_ok=True)

    work = pathlib.Path(tempfile.mkdtemp(prefix="projecta-live-"))
    config, cache = work / "config", work / "cache"
    plugins = config / "plugins"
    with zipfile.ZipFile(args.zip) as z:
        version = json.loads(z.read("meta.json"))["version"]
        ours = plugins / f"Project A_{version}"
        ours.mkdir(parents=True)
        z.extractall(ours)
    install_jellyfin_enhanced(plugins)
    cache.mkdir()
    media = work / "media"
    for d in ("movies", "shows", "anime-series", "anime-movies"):
        (media / d).mkdir(parents=True)
    os.chmod(work, 0o777)
    for p in work.rglob("*"):
        os.chmod(p, 0o777 if p.is_dir() else 0o666)

    name = CONTAINER
    docker("rm", "-f", name, check_ok=False)
    image = None
    for candidate in [args.image, args.image + ".0"]:
        if docker("pull", candidate, check_ok=False).returncode == 0:
            image = candidate
            break
    if not check("pulled the Jellyfin image", image is not None, args.image):
        sys.exit(1)
    image_id = docker("image", "inspect", "--format", "{{index .RepoDigests 0}}", image).stdout.strip()
    check("image identity recorded", True, image_id)
    docker("run", "-d", "--name", name, "-p", f"127.0.0.1:{args.port}:8096",
           "-v", f"{config}:/config", "-v", f"{cache}:/cache", "-v", f"{media}:/media:ro", image)

    srv = Server(f"http://127.0.0.1:{args.port}")
    try:
        run_checks(srv, args, config, out)
    finally:
        log = docker("logs", name, check_ok=False)
        (out / "jellyfin.log").write_text(log.stdout + log.stderr, encoding="utf-8")
        plugin_log_lines = [l for l in (log.stdout + log.stderr).splitlines() if "Project A" in l or "ProjectA" in l]
        (out / "jellyfin-projecta-lines.log").write_text("\n".join(plugin_log_lines) + "\n", encoding="utf-8")
        if not args.keep:
            docker("rm", "-f", name, check_ok=False)
            shutil.rmtree(work, ignore_errors=True)
        failed = [r for r in results if not r["ok"]]
        (out / "summary.json").write_text(json.dumps({"passed": len(results) - len(failed), "failed": len(failed), "results": results}, indent=2), encoding="utf-8")
        (out / "report.txt").write_text("\n".join(("PASS " if r["ok"] else "FAIL ") + r["check"] + (f"  ({r['detail']})" if r["detail"] else "") for r in results) + "\n", encoding="utf-8")
        print(f"\n{len(results) - len(failed)} passed, {len(failed)} failed")
        if failed:
            sys.exit(1)


def wait_ready(srv, seconds=180):
    end = time.time() + seconds
    while time.time() < end:
        try:
            status, _, info = srv.call("GET", "/System/Info/Public")
            if status == 200 and isinstance(info, dict) and info.get("Version"):
                return info
        except (urllib.error.URLError, ConnectionError, TimeoutError):
            pass
        time.sleep(2)
    return None


def passed(ep, status):
    """A caller the gate let through: exactly 200, or for endpoints with sample ids / bodies,
    anything except 401, 403 and server errors (see EndpointAccessTests.Endpoint)."""
    if ep.get("AnyStatusWhenAllowed"):
        return status not in (401, 403) and status < 500
    return status == 200


def allowed_label(ep):
    return "not 401/403/5xx" if ep.get("AnyStatusWhenAllowed") else "200"


def run_checks(srv, args, config, out):
    info = wait_ready(srv)
    if not check("server started", info is not None, info.get("Version") if info else "no answer in 180 s"):
        return

    # First-run wizard.
    srv.call("GET", "/Startup/Configuration")
    srv.call("POST", "/Startup/Configuration", body={"UICulture": "en-US", "MetadataCountryCode": "US", "PreferredMetadataLanguage": "en"})
    srv.call("GET", "/Startup/User")
    admin_pw = secrets.token_urlsafe(18)
    srv.call("POST", "/Startup/User", body={"Name": "ci-admin", "Password": admin_pw})
    srv.call("POST", "/Startup/RemoteAccess", body={"EnableRemoteAccess": True, "EnableAutomaticPortMapping": False})
    status, _, _ = srv.call("POST", "/Startup/Complete")
    check("wizard completed", status in (200, 204), str(status))

    def login(user, pw, device):
        s, _, body = srv.call("POST", "/Users/AuthenticateByName", body={"Username": user, "Pw": pw}, device=device)
        return (body["AccessToken"], body["User"]["Id"]) if s == 200 else (None, None)

    admin, _ = login("ci-admin", admin_pw, "admin")
    if not check("admin signed in", admin is not None):
        return

    def new_user(n):
        pw = secrets.token_urlsafe(18)
        s, _, body = srv.call("POST", "/Users/New", token=admin, body={"Name": n, "Password": pw})
        token, uid = login(n, pw, n)
        return token, uid, pw

    user, user_id, user_pw = new_user("ci-alice")
    disabled, disabled_id, _ = new_user("ci-disabled")
    deleted, deleted_id, _ = new_user("ci-deleted")
    _, _, u = srv.call("GET", f"/Users/{disabled_id}", token=admin)
    policy = u["Policy"]
    policy["IsDisabled"] = True
    s, _, _ = srv.call("POST", f"/Users/{disabled_id}/Policy", token=admin, body=policy)
    check("disabled a user (token taken while enabled)", s in (200, 204), str(s))
    s, _, _ = srv.call("DELETE", f"/Users/{deleted_id}", token=admin)
    check("deleted a user (token taken before deletion)", s in (200, 204), str(s))

    srv.call("POST", "/Auth/Keys?app=ProjectA-CI", token=admin)
    _, _, keys = srv.call("GET", "/Auth/Keys", token=admin)
    api_key = next((k["AccessToken"] for k in keys["Items"] if k.get("AppName") == "ProjectA-CI"), None)
    check("API key created", api_key is not None)

    # Four empty libraries, so the header has library links and gets crowded like a real
    # server's (the UI test needs Jellyfin Enhanced to fold its buttons into More).
    for lib, kind in (("Movies", "movies"), ("Shows", "tvshows"), ("Anime Series", "tvshows"), ("Anime Movies", "movies")):
        folder = lib.lower().replace(" ", "-")
        s, _, _ = srv.call("POST", f"/Library/VirtualFolders?name={urllib.parse.quote(lib)}&collectionType={kind}&refreshLibrary=false",
                           token=admin, body={"LibraryOptions": {"PathInfos": [{"Path": f"/media/{folder}"}]}})
        check(f"library '{lib}' created", s in (200, 204), str(s))

    # The plugin loaded.
    _, _, plugins = srv.call("GET", "/Plugins", token=admin)
    ours = next((p for p in plugins if p.get("Id", "").replace("-", "") == PLUGIN_GUID.replace("-", "")), None)
    check("Project A loaded and Active", ours is not None and ours.get("Status") == "Active", json.dumps(ours and {k: ours.get(k) for k in ("Name", "Version", "Status")}))
    je = next((p for p in plugins if p.get("Name") == "Jellyfin Enhanced"), None)
    check("Jellyfin Enhanced loaded and Active", je is not None and je.get("Status") == "Active", json.dumps(je and {k: je.get(k) for k in ("Version", "Status")}))

    # Record the SQLite native library the server ships (Project A uses the server's copy).
    deps = docker("exec", "projecta-live", "sh", "-c",
                  "f=$(find / -xdev -name 'jellyfin.deps.json' 2>/dev/null | head -1); grep -o -E '\"SQLitePCLRaw\\.(lib\\.e_sqlite3|core|bundle_e_sqlite3)/[0-9.]+\"' \"$f\" | sort -u",
                  check_ok=False).stdout.split()
    check("server's SQLite libraries recorded (information)", True, " ".join(deps) or "not found")

    # Injection into the real built page, alongside Jellyfin Enhanced.
    s, headers, page = srv.call("GET", "/web/", raw=True)
    html = page.decode("utf-8", "replace")
    (out / "index-served.html").write_text(html, encoding="utf-8")
    check("web page served", s == 200, str(s))
    check("our script tag injected exactly once", html.count("../Social/script?v=") == 1, str(html.count("../Social/script?v=")))
    check("Jellyfin Enhanced's tag still present exactly once", html.count("/JellyfinEnhanced/script") == 1, str(html.count("/JellyfinEnhanced/script")))
    check("our tag is before </body>", html.find("../Social/script") < html.lower().rfind("</body>"))

    # Verification 1: every endpoint, every kind of caller.
    endpoints = json.loads(pathlib.Path(args.endpoints).read_text(encoding="utf-8"))
    callers = {"no token": None, "disabled user": disabled, "deleted user": deleted, "API key": api_key, "user": user, "admin": admin}
    for ep in endpoints:
        for who, token in callers.items():
            s, headers, body = srv.call(ep["Method"], ep["Path"], token=token, raw=True)
            if ep["Anonymous"]:
                ok = s == 200
                expect = "200 (anonymous)"
            elif who == "no token":
                ok, expect = s == 401, "401"
            elif who in ("disabled user", "deleted user"):
                ok, expect = s in (401, 403), "401 or 403"
            elif who == "API key":
                ok, expect = (passed(ep, s), allowed_label(ep)) if ep["AllowApiKey"] else (s == 403, "403")
            elif who == "user":
                ok, expect = (s == 403, "403") if ep["AdminOnly"] else (passed(ep, s), allowed_label(ep))
            else:
                ok, expect = passed(ep, s), allowed_label(ep)
            check(f"{ep['Method']} {ep['Path']} as {who} -> {expect}", ok, f"got {s}")

    s, headers, body = srv.call("GET", "/Social/script", raw=True)
    lower = {k.lower(): v for k, v in headers.items()}
    check("script served as JavaScript with nosniff", "javascript" in lower.get("content-type", "") and lower.get("x-content-type-options") == "nosniff", lower.get("content-type", ""))
    check("script contains no data, only code", b"getCurrentUserId" in body and b"AccessToken" not in body)

    # Health and database.
    s, _, health = srv.call("GET", "/Social/Health", token=api_key)
    (out / "health.json").write_text(json.dumps(health, indent=2), encoding="utf-8")
    health = lowered(health)
    check("health: database ok", isinstance(health, dict) and health.get("databaseok") is True, json.dumps(health))
    check("health: script injected since start", isinstance(health, dict) and health.get("scriptinjectedsincestart") is True)
    check("health: holds no user data", isinstance(health, dict) and set(health) <= {"version", "databaseok", "databaseproblem", "schemaversion", "scriptinjectedsincestart", "scriptinjectiondisabled"}, ",".join(sorted(health)) if isinstance(health, dict) else "")
    db = config / "plugins" / "configurations" / "ProjectA" / "social.db"
    check("social.db created in plugins/configurations/ProjectA/", db.is_file())

    # Emergency switch.
    s, _, conf = srv.call("GET", f"/Plugins/{PLUGIN_GUID}/Configuration", token=admin)
    conf["DisableScriptInjection"] = True
    srv.call("POST", f"/Plugins/{PLUGIN_GUID}/Configuration", token=admin, body=conf)
    _, _, page = srv.call("GET", "/web/", raw=True)
    html_off = page.decode("utf-8", "replace")
    check("emergency switch: page served without our tag", "../Social/script" not in html_off)
    check("emergency switch: Jellyfin Enhanced unaffected", html_off.count("/JellyfinEnhanced/script") == 1)
    s, _, health = srv.call("GET", "/Social/Health", token=api_key)
    health = lowered(health)
    check("emergency switch: shown in health", isinstance(health, dict) and health.get("scriptinjectiondisabled") is True)
    conf["DisableScriptInjection"] = False
    srv.call("POST", f"/Plugins/{PLUGIN_GUID}/Configuration", token=admin, body=conf)
    _, _, page = srv.call("GET", "/web/", raw=True)
    check("emergency switch off again: tag back", page.decode("utf-8", "replace").count("../Social/script?v=") == 1)

    run_verify_auth_script(srv, admin, "ci-alice", user_pw, api_key, out)

    # Credentials for the UI test (throwaway server only), never printed.
    if args.creds:
        creds = pathlib.Path(args.creds).resolve()
        if out.resolve() in creds.parents:
            sys.exit("--creds must be outside the published results folder")
        creds.write_text(json.dumps({"base": srv.base, "user": "ci-alice", "password": user_pw}), encoding="utf-8")
        os.chmod(creds, 0o600)


def run_verify_auth_script(srv, admin, user_name, user_pw, api_key, out):
    """
    Runs tests/live/verify-auth.sh exactly as VERIFY.md step 8 tells Michael to, so the
    script he runs on his server is itself tested against a real Jellyfin first. Answers its
    prompts in order (ordinary user, throwaway user, Enter after disabling it, API key),
    disables the throwaway user at the pause, then checks the result line VERIFY.md promises
    and that no password, token or key appears in the output.
    """
    script = REPO / "tests" / "live" / "verify-auth.sh"
    endpoints = REPO / "tests" / "live" / "endpoints.tsv"
    throwaway, throwaway_pw = "ci-script-disabled", secrets.token_urlsafe(18)
    srv.call("POST", "/Users/New", token=admin, body={"Name": throwaway, "Password": throwaway_pw})
    _, _, users = srv.call("GET", "/Users", token=admin)
    throwaway_id = next(u["Id"] for u in users if u["Name"] == throwaway)

    proc = subprocess.Popen(["bash", str(script), str(endpoints)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                            stderr=subprocess.STDOUT, text=True, env={**os.environ, "CONTAINER": CONTAINER})
    output = []
    try:
        proc.stdin.write(f"{user_name}\n{user_pw}\n{throwaway}\n{throwaway_pw}\n")
        proc.stdin.flush()
        for line in proc.stdout:  # wait for the script's pause, as Michael would
            output.append(line)
            if "Now, in Jellyfin" in line:
                break
        _, _, u = srv.call("GET", f"/Users/{throwaway_id}", token=admin)
        policy = u["Policy"]
        policy["IsDisabled"] = True
        srv.call("POST", f"/Users/{throwaway_id}/Policy", token=admin, body=policy)
        proc.stdin.write(f"\n{api_key}\n")
        proc.stdin.close()
    except BrokenPipeError:
        pass  # the script stopped early; its output says why
    output.append(proc.stdout.read())
    proc.wait(timeout=180)
    text = "".join(output)
    (out / "verify-auth.log").write_text(text, encoding="utf-8")

    result = next((l.strip() for l in text.splitlines() if l.startswith("Result:")), "no result line")
    check("verify-auth.sh (VERIFY.md step 8) exits cleanly", proc.returncode == 0, f"exit {proc.returncode}")
    # One check per anonymous endpoint, four (no token, disabled, API key, user) for the rest.
    rows = [l.split("\t") for l in endpoints.read_text(encoding="utf-8").splitlines() if l and not l.startswith("#")]
    expected = sum(1 if r[2] == "yes" else 4 for r in rows)
    check("verify-auth.sh reports what VERIFY.md promises", result == f"Result: {expected} passed, 0 failed.", f"{result} (expected {expected})")
    check("verify-auth.sh output holds no password, token or key",
          not any(secret in text for secret in (user_pw, throwaway_pw, api_key)))


if __name__ == "__main__":
    main()
