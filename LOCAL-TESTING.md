# Local BellyCarry testing

Run a host and a client on this Linux machine, switching between two 960×640
windows. Both use the installed KrokMP and BellyCarry DLLs over direct-IP UDP
7790. Steam accounts and port forwarding are not needed.

From the game directory:

```bash
./modding/local-coop.sh           # prepare, start host, wait for it, connect client
./modding/local-coop.sh status
./modding/local-coop.sh stop      # stops only the launcher-owned test instances
```

The first launch creates two new Proton prefixes and can take a few minutes.
The script uses `umu-run`, `rsync`, Python 3, and an installed CachyOS SLR or
GE-Proton. Override Proton with `--proton /path/to/Proton` or `BC_PROTONPATH`.
Startup progress goes to the terminal; a failure reports the relevant log.
On a startup timeout, the window is left running so you can inspect it.
Automatic runtime updates are skipped when a runtime already exists; set
`UMU_RUNTIME_UPDATE=1` to opt in. A missing runtime is still installed by umu.

Once both players connect, **start a run from BC_Host's window**. The host enables
`sv_cheats` and `AllowClientCheatCommands` for this test session. KrokMP's startup
options keep the game running when a window loses focus.

## Isolation and refreshing mods

Each role has its own directory under `modding/.local-coop/host/` or `client/`:

- `game/`: independent game and mod files, including `cu_mcp_config.json`.
- `prefix/`: independent Proton/Wine prefix, including game settings and saves.
- `game/BepInEx/LogOutput.log`: game/plugin diagnostics, including `[BC]` traces.
- `logs/Player.log` and `logs/launcher.log`: Unity and Proton/launcher output.
- `logs/<timestamp>-*.log`: previous launch logs.

The normal Steam installation and its saves are not changed. Test copies omit
Experiment NPCs (it explicitly does not support KrokMP), KrokMP's updater plugin,
and updater patchers. Other currently installed plugins and their assets are
copied, including CUCoreLib, Consumed, XL, Kitchen, NoTimeLimit, and BellyCarry.
Disabled plugins are not re-enabled. Test BepInEx configs are seeded once and
then retained; game files and plugin files are refreshed at every launch.

After rebuilding BellyCarry, stop and rerun the launcher to copy the new DLL to
both instances. The regular `./modding/build.sh BellyCarry` still deploys to your
normal installed game first.

Additional commands:

```bash
./modding/local-coop.sh prepare             # copy files without opening windows
./modding/local-coop.sh start --role host   # start only the host
./modding/local-coop.sh start --role client # connect to an already running host
./modding/local-coop.sh stop --role client
```

This KrokMP build's `--ksmulti-startclient` code hard-codes `localhost:7790`,
which its address parser mishandles and reports as "Unknown Host". The launcher
uses `--ksmulti-runcommand connect_127.0.0.1:7790` (and the corresponding host
command) to avoid this. It checks port 7790 before starting a host. Close another
KrokMP host if it occupies that port.

## Two independent MCP connections

The existing C# bridge already reads `http_url` from `cu_mcp_config.json` next to
the game executable. No bridge DLL rebuild is required. The generated defaults
are host **8766**, client **8767**, leaving the normal bridge's **8765** alone.
`ai_player_enabled` is false: each bridge controls that instance's actual player.

The project's `.codex/config.toml` registers **cu-host** and **cu-client** for
Codex. Start a new Codex session in this trusted project to load them. This uses
the [documented project MCP configuration](https://learn.chatgpt.com/docs/extend/mcp?surface=cli).
The 150-second tool timeout allows the bridge's 120-second order timeout to finish.

For other MCP clients, the launcher writes `modding/.local-coop/mcp.json`; merge
its two `mcpServers` entries into the client's configuration. Each entry runs the
same wrapper with a different role:

```bash
./modding/tools/mcp-instance.py host
./modding/tools/mcp-instance.py client
```

These are **stdio MCP servers**, launched and kept alive by your MCP client;
they are not shell prompts or standalone HTTP APIs for issuing orders. Starting
the game launcher alone does not start these servers. The game bridges retry
their connections, so the game and MCP client can start in either order.

The wrapper uses the existing bridge project's `.venv` and server implementation
at `/mnt/steamdrive/modelStuff/AIHelpers/mcp-servers/cu-mcp-bridge`. It initializes
the HTTP listener synchronously (port collisions fail immediately) and routes
diagnostic prints to stderr so stdout remains valid MCP protocol traffic.
The shared server files and existing global MCP registrations are unchanged.

Custom endpoints and server location:

```bash
./modding/local-coop.sh prepare --host-mcp-port 8876 --client-mcp-port 8877
./modding/local-coop.sh start --host-mcp-port 8876 --client-mcp-port 8877
./modding/local-coop.sh prepare --bridge-dir /path/to/cu-mcp-bridge
./modding/tools/mcp-instance.py host --server-dir /path/to/cu-mcp-bridge
```

The wrapper reads its role's generated port at startup. Restart the MCP server
after changing ports. `mcp-instance.py --http-port PORT` also provides an explicit
override; that must match the game's `http_url`. Only one MCP server should own
each port. Keep the custom port arguments on subsequent game starts, since
omitting them restores 8766/8767.

## BellyCarry checks

### Expected visual behavior

When a carry succeeds, it should look like Consumed eating a trader: the carried
player's entire body and multiplayer nametag disappear, while the carrier shows
Consumed's normal stomach bulge, stomach animation, and stomach sounds. The
passenger remains networked and can struggle or force their way out; hiding the
renderers must not disable the passenger's `Body` or `NetBody` components.

The old path only set `Carries[passenger] = carrier`. In a real session the
driver could miss the first network transition, leaving `HiddenSprites=0` and
`BellyEntries=0`; this produced a visible piggyback. The fix applies the visual
layer immediately from `ApplyGrab` and registers BellyCarry's network receivers
when KrokMP creates its transport. It hides all `Renderer` components (including
the nametag), then continuously reasserts that state because Consumed's sprite
controller can apply new sprites during animation. It also adds the synthetic
`VoredGameObject` to the carrier so the existing Consumed stomach renderer drives
the same appearance.

1. Make BC_Client carryable/downed, then use BC_Host's carry interaction (the
   installed default is **O**; check the KrokMP keybind settings if changed).
2. Check passenger hiding, the carrier's belly, and both players' moodles.
3. Press jump once as passenger: check squirming without immediate release.
4. Mash jump: check the configured force-out threshold (default five taps).
5. Carry again and regurgitate (H) from the carrier: check release and restored
   sprites. Repeat with BC_Client carrying BC_Host.
6. Run `bcdump` in each game's console around a failure. It writes into that
   instance's BepInEx log. Through MCP, use that role's `send_order` with
   `action: "console"`, `parameters: {"command": "bcdump"}` during a run.

For a successful carry, `bcdump` should show `Carries(1): 1=>0` and nonzero
`HiddenSprites`, `HiddenRenderers`, `BellyEntries`, and carrier `stomach`/`fill` values on both
instances. After release all tracking collections should return to zero and
both bodies should render again.

A localhost connection exercises both actual network roles. It does not reproduce
internet latency or packet loss, so timing-sensitive fixes may still need those
conditions tested later.

### Jump regression note

Jumping while carrying must preserve the belly carry. KrokMP's multiplayer jump
prefix calls `StopPiggyback` on the jumper, including a carrier, so BellyCarry's
jump patch temporarily suppresses that internal stop while allowing the carrier's
normal jump. A passenger's jump remains intercepted as a struggle; only the
configured mash threshold forces release. If either view shows the passenger or
nametag after a single jump, run `bcdump` on both instances and check that
`Carries`, `HiddenRenderers`, and `BellyEntries` remain populated.

### Carry menu versus a dedicated visual command

The current tests intentionally use KrokMP's existing **Carry** action in the
player interaction menu. BellyCarry treats that successful carry transition as
the trigger and replaces its presentation with the Consumed belly visual, so no
extra keybind is required for normal play. If the menu hook becomes unstable or
conflicts with another mod, it is acceptable to move the visual transition to a
dedicated BellyCarry command/key while leaving KrokMP's native Carry behavior
available as a fallback. Do not add a second interaction solely for testing
unless the existing Carry path cannot be made reliable.

### Jump detach and stomach icon follow-up

`StopPiggyback` is now blocked locally whenever a BellyCarry entry is active,
including KrokMP's jump path. Release removes the BellyCarry entry first, so
regurgitate and force-out still detach normally. Each carry also creates an
inactive `trader1` placeholder object for Consumed's existing circular stomach
UI; it is removed with the carry entry. This gives a visible stomach-content
icon while keeping the passenger body/network object separate and hidden.

### Carry rule independence

BellyCarry now has `General/AllowConsciousCarry` (enabled by default). When
`Enabled` and `ReplaceCarry` are on, it patches KrokMP's
`CanBeCarriedBySomeone()` result so the existing Carry menu action works without
enabling the global `AlwaysAllowCarry` multiplayer rule. Disable this option to
restore KrokMP's normal downed-only restriction.

The active carry broadcast uses KrokMP's very-reliable transport path. This is
needed for clients that otherwise receive the piggyback attachment but miss the
BellyCarry visual-state message.

If the custom message is missed, the client driver infers a BellyCarry entry from
KrokMP's synchronized `piggybacking_on` link and cleans it up when that link ends.
This fallback prevents a visible passenger even when custom transport delivery is
unavailable.

The client fallback checks both KrokMP relationship fields (`piggybacking_on` and
`carrying_person`), because ownership determines which half is synchronized on a
given view.

The immediate client fallback also runs from BellyCarry's `StartPiggyback` postfix,
not only from the periodic driver scan. This handles clients whose driver update
is delayed while KrokMP has already synchronized the attachment.

Passenger jump struggles now use strength `1.5` (up from `0.15`) so Consumed's
stomach view gets a visible shake and the impulse is noticeable. Gurgle audio is
still subject to Consumed's built-in random sound gate. Escape after repeated
jumps is controlled by `AllowPassengerForceOut` and `ForceOutTaps` (default five
presses in four seconds); set `AllowPassengerForceOut = false` to disable it.

### Weight growth guard

Consumed computes weight gain inside `BodyVoreController.Update` before the
regular BellyCarry driver runs. The mod now zeros a carrier's digestion rate in
a Harmony prefix before that calculation, preventing `weightOffset` from growing
on every frame. The saved digestion rate is restored immediately when the final
carry is released.

### Release-state fallback

On release, clients now also clear BellyCarry state from KrokMP's authoritative
`StopPiggyback` detach. This restores passenger renderers and removes the stomach
entry even if the custom inactive message is missed, so the carrier and passenger
moodles/status return to normal.

The digestion guard uses `TryGetValue` when resolving carry ownership, avoiding a
`KeyNotFoundException` during the same-frame release cleanup race.

The client `StartPiggyback` fallback accepts both successful and already-linked
sync calls. KrokMP may report `result=false` while the relationship is already
present; the linked-state check still applies the BellyCarry visuals in that
case.

### Future interaction and downed-player design

A future implementation may add a BellyCarry-specific action to KrokMP's player
interaction menu (alongside Inspect Wounds, Inventory, and Push) instead of
reusing the native Carry/piggyback framework. That action would send BellyCarry's
own attach/detach messages and avoid KrokMP's carry-rule and jump behavior. Keep
the current Carry hook as the compatibility path until the menu integration is
implemented and tested.

Possible later gameplay for downed passengers: a carried player could be marked
as downed and either digest over time or receive healing while inside. This needs
an explicit mode/configuration, health and death synchronization, a safe release
path, and clear UI/audio feedback. The current build never damages or heals a
passenger and keeps digestion disabled.

When a client-owned carry arrives through KrokMP sync without a custom active
message, the client fallback now publishes that inferred relationship back to the
host once. This keeps the host's belly visual and status synchronized when the
client is the carrier.

The linked-state fallback now runs on both client and host. This covers the host
being the passenger in a client-owned carry, ensuring the host has BellyCarry
state before a jump can be processed and cannot escape with one press.

From the game root, restart both isolated instances with:

```bash
./restart-local-coop.sh
```

The wrapper performs `stop` followed by `start` and accepts the same options as
`modding/local-coop.sh` (for example `--role host`).

Passenger jump protection also covers KrokMP's server-relayed `Body.Jump` calls,
not only the local passenger body. This prevents a host passenger's single jump
from being converted into a server-side `StopPiggyback`.

The jump guard now also suppresses the `StopPiggyback` postfix when its original
method is skipped. Without that check, Harmony could still run the release
postfix after a blocked jump and broadcast an unintended `active=false`.

Server resync now reapplies `ApplyCarryVisuals` locally before rebroadcasting each
active entry, keeping the host's view of a client-owned carrier's belly current.

Intentional release (H regurgitate or force-out) sets a two-second suppression
window for client piggyback fallback inference. This prevents the still-linked
KrokMP sync packet from immediately re-creating the BellyCarry entry after a
successful release.

Release suppression was extended to five seconds, and client-side release now
explicitly calls `StopPiggyback` after clearing BellyCarry state so stale local
links cannot trigger immediate re-entry.

Release suppression now persists until both KrokMP relationship fields are clear,
rather than expiring after a fixed timeout. This prevents stale attachment packets
from re-entering a passenger after escape.

While release suppression is active, BellyCarry also blocks new KrokMP
`StartPiggyback` calls for that passenger. This prevents stale sync retries from
physically reattaching the passenger to the carrier's back after escape.

Authoritative carry discovery also hooks KrokMP's internal
`HandlePiggybackUpdate`. This captures client-owned carries that bypass the
interaction hook and ensures the host records/broadcasts the active BellyCarry
state.

Release desync recovery now runs from `HandlePiggybackUpdate`: if a client sees a
body with neither `piggybacking_on` nor `carrying_person` but still has a
BellyCarry entry, it clears the stale entry and restores visuals locally.

Per-body `NetBody.Update` now performs the final fallback reconciliation each
frame. It reapplies active visuals from either relationship direction and clears
stale passenger entries as soon as the network relationship disappears.

### Poisoned-session recovery

If repeated carry/release tests leave stale local state, run `bcrecover` in that
instance's console. It clears carry mappings, suppression timers, digestion
bookkeeping, belly entries, and restores hidden renderers without restarting the
game. Use it on both instances before a clean regression run.

Distance-based KrokMP detach is allowed to proceed normally; only jump-triggered
stops are suppressed. Client detach handling marks distance/death releases as
suppressed too, preventing delayed sync packets from pulling the passenger back in.
