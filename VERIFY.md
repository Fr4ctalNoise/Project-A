# Verifying Project A on your server

Run these after installing or updating. Each step says whether it **only reads** or
**changes** something, what its output may contain, and what to send back. Stop at the
first step that doesn't match and send what you saw.

These steps are for **0.1.0.0 (the Spike)**. Container name `Jellyfin`, config folder
`/mnt/user/appdata/Jellyfin`.

---

## 1. Back up Jellyfin's settings and databases (changes: creates one file; Jellyfin stops briefly)

```bash
mkdir -p /mnt/user/appdata/backups
docker stop Jellyfin
tar -czf "/mnt/user/appdata/backups/jellyfin-config-$(date +%F-%H%M).tar.gz" \
    -C /mnt/user/appdata/Jellyfin --exclude=./cache --exclude=./metadata --exclude=./log --exclude=./transcodes .
docker start Jellyfin
ls -lh /mnt/user/appdata/backups/
```

**Expect:** a `jellyfin-config-….tar.gz` of a few MB to a few hundred MB. Jellyfin is down for
under a minute. This copy holds your users and settings: don't share it.

## 2. Stop plugins updating themselves (changes: one setting; once only)

Dashboard → **Scheduled Tasks** → **Update Plugins** → delete both triggers (the startup one and
the every-24-hours one). Plugins then update only when you click **Update**. Project A also
ships with `autoUpdate: false`, so it never updates itself either way.

## 3. Add the repository and install (changes: installs the plugin)

1. Dashboard → **Plugins** → **Repositories** → **+**
   - Name: `Project A`
   - URL: `https://raw.githubusercontent.com/Fr4ctalNoise/Project-A/main/manifest.json`
2. **Catalog** → **Project A** → version **0.1.0.0** → **Install**.
3. Restart Jellyfin:
   ```bash
   docker restart Jellyfin
   ```

## 4. Check it loaded (only reads)

```bash
docker logs Jellyfin 2>&1 | grep -i -E "project ?a" | tail -20
ls -la "/mnt/user/appdata/Jellyfin/plugins/" | grep -i "project a"
```

Open Jellyfin in your browser once, then:

```bash
docker logs Jellyfin 2>&1 | grep -i "Project A: added its script" | tail -1
```

**Expect:**
- Dashboard → Plugins → **Project A 0.1.0.0**, status **Active**.
- A folder `Project A_0.1.0.0` containing exactly `Jellyfin.Plugin.ProjectA.dll`, `meta.json`,
  `LICENSE`, `THIRD_PARTY_NOTICES.md` and `licenses/`.
- The log line "Project A: added its script to the web page".

**Safe to send:** these lines contain plugin names, versions and folder paths only.

## 5. The database (only reads)

Project A creates its database the first time the health check (step 8) or a later feature
uses it. After step 8:

```bash
ls -la /mnt/user/appdata/Jellyfin/plugins/configurations/ProjectA/
docker logs Jellyfin 2>&1 | grep -i "Project A: database" | tail -3
```

**Expect:** `social.db` (plus `social.db-wal` / `-shm` while Jellyfin runs), and
"Project A: database ready at …/social.db (schema 1)". If you see "database check failed"
instead, send that line: it names a problem code (for example `not_writable`), never your data.

## 6. Every plugin's script is on the page exactly once (only reads)

```bash
docker exec Jellyfin curl -s http://localhost:8096/web/ | grep -o -E 'src="[^"]*(script|\.js)[^"]*"' | sort | uniq -c
```

**Expect:** a count of `1` for every line, including `../Social/script?v=0.1.0.0` and
`../JellyfinEnhanced/script?v=…`. Anything with a `2` means two plugins injected the same
script.
**Safe to send:** the output is script addresses and version numbers only.

## 7. The button and the page (only reads; in your browser)

Press **Ctrl+F5** on Jellyfin's home page.

1. **Full-width window:** an **Activity Feed** button (the stacked-cards icon) sits in Jellyfin
   Enhanced's group, after the dice.
2. **Narrow window** (about a third of the screen): it disappears from the bar; open **More** and
   it is listed as **Activity Feed**.
3. Click it (or choose it in More): the address ends in `#/social` and the page shows the
   **Activity Feed** heading in your theme's heading style with "Project A is installed and
   working. The feed arrives in the next release."
4. Click **Home**: your home page comes back.
5. Sign out, then open `…/web/#/social`: you get Jellyfin's sign-in screen; no Project A button
   or page.
6. Sign in as `pa-tester` (a normal user): the button is there too.

**Send:** pass/fail for each. For a screenshot, crop to the header or the page; the header shows
your server and library names.

**One thing I couldn't check from here:** how far the page starts below the header (step 3).
Tell me if the heading is hidden under the header or has a large gap above it.

## 8. The sign-in test (verification 1) (changes: two sign-ins and sign-outs; you disable one test user)

You need:
- `pa-tester`: an ordinary user (not an administrator).
- `pa-disabled`: a throwaway user, **enabled for now**. The script asks you to disable it halfway.
- An API key named `pa-verify` (Dashboard → **API Keys** → **+**). Create it when the script asks.

```bash
cd /tmp
curl -fsSLO https://raw.githubusercontent.com/Fr4ctalNoise/Project-A/v0.1.0.0/tests/live/verify-auth.sh
curl -fsSLO https://raw.githubusercontent.com/Fr4ctalNoise/Project-A/v0.1.0.0/tests/live/endpoints.tsv
less verify-auth.sh          # read it first: q to quit
bash verify-auth.sh endpoints.tsv
```

The script asks for each password and the API key at **hidden** prompts. It never puts them in
a command, a file or on screen. Calls run inside the container with `curl`.

**Expect:** `Result: 5 passed, 0 failed.`, and a health check line like
`{"Version":"0.1.0.0","DatabaseOk":true,"DatabaseProblem":null,"SchemaVersion":1,"ScriptInjectedSinceStart":true,"ScriptInjectionDisabled":false}`.

**Safe to send:** the whole output. It lists endpoint paths, status codes and the health line,
with no tokens, passwords or user data.

**Afterwards (changes):** delete the API key `pa-verify` and the user `pa-disabled`, then
remove the script:

```bash
rm -f /tmp/verify-auth.sh /tmp/endpoints.tsv
```

## 9. The emergency switch (changes a setting, then back)

1. Dashboard → **Plugins** → **Project A** → tick **Turn off Project A in the web interface** →
   **Save**.
2. Ctrl+F5: the Activity Feed button is gone; everything else, including Jellyfin Enhanced, is
   unchanged.
3. Untick it, **Save**, Ctrl+F5: the button is back.

## If something goes wrong: roll back (changes: removes the plugin)

```bash
docker stop Jellyfin
rm -rf "/mnt/user/appdata/Jellyfin/plugins/Project A_0.1.0.0"
docker start Jellyfin
```

This removes the plugin; its database stays in `plugins/configurations/ProjectA/` and is
harmless. To remove that too:

```bash
rm -rf /mnt/user/appdata/Jellyfin/plugins/configurations/ProjectA
```

If Jellyfin itself won't start, restore the step 1 backup:

```bash
docker stop Jellyfin
tar -xzf /mnt/user/appdata/backups/jellyfin-config-<date>.tar.gz -C /mnt/user/appdata/Jellyfin
docker start Jellyfin
```
