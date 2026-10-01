using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace CreatureControl
{
    /// <summary>
    /// Keeps the zones around each logging leash genuinely loaded and
    /// simulated, so trolls keep working for real while the player is
    /// elsewhere - actual trees felled, actual rocks broken, world state
    /// really changing - rather than being credited an estimate on return.
    ///
    /// The mechanism, read out of ZoneSystem rather than guessed:
    ///
    ///     PokeLocalZone(zoneID)  resets that zone's m_ttl to 0, or spawns
    ///                            the zone outright if it isn't loaded yet
    ///     UpdateTTL()            destroys any zone whose m_ttl has run past
    ///                            m_zoneTTL and holds no live instances
    ///
    /// CreateLocalZones(refPoint) pokes the whole simulation-radius block
    /// around a point, which is exactly what the game itself does every tick
    /// for the player. Calling it for a leash position does the same job for
    /// that leash - no teardown patching needed, nothing fought, just the
    /// same keep-alive the player already gets, pointed somewhere else.
    ///
    /// THE COST IS REAL. Every kept zone is fully simulated: terrain,
    /// vegetation, physics and every creature in it, continuously, whether
    /// or not anyone is looking. Zones are 64m, so one leash spans several.
    /// That is the whole reason this is a switch rather than always-on.
    ///
    /// Leash positions are remembered independently and persisted to disk,
    /// because of a chicken-and-egg problem: once a zone unloads, the leash
    /// piece inside it unloads too, so there would be nothing left to find
    /// and nothing to keep alive. Remembering the coordinates separately is
    /// what lets a leash hold its own area open without the player ever
    /// going back to it.
    /// </summary>
    public static class ZoneKeepAlive
    {
        delegate bool PokeLocalZoneDel(ZoneSystem self, Vector2s zoneID);
        static PokeLocalZoneDel _pokeLocalZone;
        static bool _bound;

        /// <summary>Exactly one 64m zone per leash - never the surrounding
        /// block. CreateLocalZones pokes a whole NearSimulationDistance
        /// square around a point (what the game does for the player), which
        /// for one leash meant holding nine or more zones open at once. That
        /// is both far more simulation than the job needs and far more
        /// terrain to get half-initialised, which is the likeliest source of
        /// the split/see-through ground. A leash bigger than 64m simply has
        /// its work area clipped to the chunk it stands in.</summary>
        static readonly SimulationDistance SingleZone = new SimulationDistance(0, 0);

        static readonly List<Vector3> _leashPositions = new List<Vector3>();

        /// <summary>Every zone a leash's work circle touches, rebuilt each
        /// poke. One zone per leash clipped the work area: a leash near a
        /// chunk edge with a 50m radius reaches into up to nine zones, and
        /// trees or chests outside the one held zone simply didn't exist
        /// while the player was away.</summary>
        static readonly List<Vector2s> _heldZones = new List<Vector2s>();
        static readonly List<ZDO> _claimBuf = new List<ZDO>();
        static int _lastClaimed;
        static float _nextPoke;
        static string _storePath;
        static bool _loaded;

        static void Bind()
        {
            if (_bound) return;
            _bound = true;
            try
            {
                // PokeLocalZone, NOT CreateLocalZones: the latter pokes the
                // whole simulation block around a point. This holds exactly
                // the one zone asked for.
                var m = AccessTools.Method(typeof(ZoneSystem), "PokeLocalZone", new[] { typeof(Vector2s) });
                if (m != null) _pokeLocalZone = AccessTools.MethodDelegate<PokeLocalZoneDel>(m);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[keepalive] could not bind ZoneSystem.PokeLocalZone: {e.Message}");
            }

            if (_pokeLocalZone == null)
                Plugin.Log.LogWarning(
                    "[keepalive] disabled this session - ZoneSystem.PokeLocalZone could not be resolved.");
            else
                Plugin.Log.LogInfo("[keepalive] bound to ZoneSystem.PokeLocalZone - holding ONE zone per leash.");
        }

        public static void Init(string configDir)
        {
            _storePath = Path.Combine(configDir ?? ".", "CreatureControl.Leashes.txt");
        }

        /// <summary>Records a leash's position so its area can be held open
        /// even after the leash itself unloads. Called as leashes are seen.</summary>
        public static void Remember(Vector3 pos)
        {
            for (int i = 0; i < _leashPositions.Count; i++)
            {
                // Same leash, near enough - don't accumulate duplicates as it
                // drifts by a few centimetres between reads.
                if ((_leashPositions[i] - pos).sqrMagnitude < 4f) return;
            }
            _leashPositions.Add(pos);
            Save();
            if (Plugin.Verbose)
                Plugin.Log.LogInfo($"[keepalive] remembered leash at {pos} ({_leashPositions.Count} total).");
        }

        public static void Tick()
        {
            if (!Plugin.KeepLeashZonesLoaded) return;
            if (ZoneSystem.instance == null || ZNet.instance == null) return;

            EnsureLoaded();
            Bind();
            if (_pokeLocalZone == null) return;
            if (_leashPositions.Count == 0) return;

            if (Time.time < _nextPoke) return;
            _nextPoke = Time.time + 0.5f;

            // Spawns each zone if missing, resets its TTL if it's already
            // up, so UpdateTTL never reaches it.
            RebuildHeldZones();
            for (int i = 0; i < _heldZones.Count; i++)
                _pokeLocalZone(ZoneSystem.instance, _heldZones[i]);

            ClaimOwnership();

            // The decisive check: is the leash's own zone actually loaded
            // right now, and how far is the player from it? If "loaded=True"
            // holds while distance is large, the hold is genuinely working.
            // If it flips to False as soon as you walk away, poking alone
            // isn't enough and something else is tearing the zone down.
            if (!Plugin.Verbose || Time.time < _nextReport) return;
            _nextReport = Time.time + 10f;

            var player = Player.m_localPlayer;
            for (int i = 0; i < _leashPositions.Count; i++)
            {
                var p = _leashPositions[i];
                bool loaded = ZoneSystem.instance.IsZoneLoaded(p);
                float dist = player != null ? Vector3.Distance(player.transform.position, p) : -1f;
                int nearby = CountLoggersNear(p);
                Plugin.Log.LogInfo(
                    $"[keepalive] leash {i} at {p}: zoneLoaded={loaded}, " +
                    $"playerDist={dist:0}m, loggingTrollsLoaded={nearby}, heldZones={_heldZones.Count}, " +
                    $"claimed={_lastClaimed}, " +
                    $"zdoAppend[{Patch_ZDOMan_FindSectorObjects_LeashSectors._appendReport}]");
            }
        }

        static float _nextReport;

        static void RebuildHeldZones()
        {
            _heldZones.Clear();
            float r = Plugin.LoggingLeashRadius;
            for (int i = 0; i < _leashPositions.Count; i++)
            {
                var p = _leashPositions[i];
                var lo = ZoneSystem.GetZone(p - new Vector3(r, 0f, r));
                var hi = ZoneSystem.GetZone(p + new Vector3(r, 0f, r));
                for (int x = lo.x; x <= hi.x; x++)
                    for (int y = lo.y; y <= hi.y; y++)
                    {
                        var z = new Vector2s(x, y);
                        if (!_heldZones.Contains(z)) _heldZones.Add(z);
                    }
            }
        }

        /// <summary>THE FREEZE FIX, part 2. A loaded object only runs its AI
        /// and physics on whichever machine OWNS it, and vanilla only ever
        /// claims ownership of objects near the player - so anything in a
        /// held zone that has lost its owner (walking away releases it, and a
        /// world load starts everything unowned) sits there frozen. Claims
        /// only objects nobody owns, so it never takes anything off another
        /// player in multiplayer.</summary>
        static void ClaimOwnership()
        {
            var zdoman = ZDOMan.instance;
            if (zdoman == null) return;
            long me = ZDOMan.GetSessionID();
            int claimed = 0;
            for (int i = 0; i < _heldZones.Count; i++)
            {
                if (!ZoneSystem.instance.IsZoneLoaded(_heldZones[i])) continue;
                _claimBuf.Clear();
                zdoman.FindSectorObjects(_heldZones[i], SingleZone, _claimBuf);
                for (int j = 0; j < _claimBuf.Count; j++)
                {
                    var zdo = _claimBuf[j];
                    if (zdo == null || zdo.HasOwner()) continue;
                    zdo.SetOwner(me);
                    claimed++;
                }
            }
            if (claimed > 0) _lastClaimed = claimed;
        }

        /// <summary>How many tracked logging creatures are currently INSTANTIATED
        /// near this leash. Zone loaded but zero trolls means the zone is being
        /// held but the creature isn't being spawned into it - a different
        /// problem from the zone unloading, and worth telling apart.</summary>
        static int CountLoggersNear(Vector3 pos)
        {
            int n = 0;
            foreach (var st in CreatureState.AllTracked)
            {
                if (st == null || st.Chr == null || !st.IsLogging) continue;
                if ((st.Chr.transform.position - pos).sqrMagnitude <=
                    Plugin.LoggingLeashRadius * Plugin.LoggingLeashRadius) n++;
            }
            return n;
        }

        static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            if (string.IsNullOrEmpty(_storePath) || !File.Exists(_storePath)) return;

            try
            {
                foreach (var line in File.ReadAllLines(_storePath))
                {
                    var parts = line.Split(',');
                    if (parts.Length != 3) continue;
                    if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                        float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                        float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                    {
                        _leashPositions.Add(new Vector3(x, y, z));
                    }
                }
                if (Plugin.Verbose)
                    Plugin.Log.LogInfo($"[keepalive] loaded {_leashPositions.Count} remembered leash position(s).");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[keepalive] could not read remembered leashes: {e.Message}");
            }
        }

        static void Save()
        {
            if (string.IsNullOrEmpty(_storePath)) return;
            try
            {
                var lines = new List<string>(_leashPositions.Count);
                foreach (var p in _leashPositions)
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}", p.x, p.y, p.z));
                File.WriteAllLines(_storePath, lines);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[keepalive] could not save remembered leashes: {e.Message}");
            }
        }

        /// <summary>Remembered leash positions, for the ZDO-gathering patch
        /// below. Read-only use only.</summary>
        public static List<Vector3> Positions
        {
            get { EnsureLoaded(); return _leashPositions; }
        }

        /// <summary>Holding the ZONE open is only half the job, and the
        /// diagnostic proved it: at 3.5km away the leash reported
        /// zoneLoaded=True with loggingTrollsLoaded=0. ZoneSystem governs the
        /// zone's own terrain and props, but which ZDOs become real
        /// GameObjects is decided separately, by ZNetScene:
        ///
        ///     var zone = ZoneSystem.GetZone(ZNet.instance.GetReferencePosition());
        ///     ZDOMan.instance.FindSectorObjects(zone, ..., m_tempCurrentObjects, ...);
        ///     CreateObjects(m_tempCurrentObjects, ...);
        ///     RemoveObjects(m_tempCurrentObjects, ...);
        ///
        /// That list is built around the PLAYER's zone only, so a troll in a
        /// held zone is never created - and note RemoveObjects tears down
        /// anything absent from the same list, so merely spawning it would
        /// not survive the next frame either.
        ///
        /// Appending the leash sectors to that one list solves both at once:
        /// the objects get created, and because they're now in the list
        /// RemoveObjects no longer considers them strays. FindSectorObjects
        /// is public, so this needs no transpiler and no reimplementation of
        /// vanilla's create/destroy pass.
        /// </summary>
        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.FindSectorObjects),
            new[] { typeof(Vector2s), typeof(SimulationDistance), typeof(List<ZDO>), typeof(List<ZDO>) })]
        static class Patch_ZDOMan_FindSectorObjects_LeashSectors
        {
            static bool _reentry;
            internal static string _appendReport = "(no append yet)";
            static readonly List<ZDO> _buf = new List<ZDO>();
            static readonly HashSet<ZDO> _seen = new HashSet<ZDO>();

            static void Postfix(ZDOMan __instance, SimulationDistance simulationDistance, List<ZDO> sectorObjects)
            {
                if (!Plugin.KeepLeashZonesLoaded || sectorObjects == null) return;
                if (_reentry) return;   // this calls back into the same method

                // THE FREEZE FIX, part 1. FindSectorObjects has more than one
                // caller. ZNetScene's create/destroy pass is the one this is
                // for; ZDOMan.ReleaseNearbyZDOS is another, and it strips
                // ownership from anything it is handed that lies outside the
                // player's active area - which, fed the leash sectors, meant
                // the troll and its trees lost their owner every 2 seconds and
                // stood frozen. Only append while ZNetScene is asking.
                if (!Patch_ZNetScene_CreateDestroyObjects_Scope.Active) return;

                if (_heldZones.Count == 0) return;

                _reentry = true;
                try
                {
                    _seen.Clear();
                    for (int i = 0; i < sectorObjects.Count; i++) _seen.Add(sectorObjects[i]);

                    var zs = ZoneSystem.instance;
                    if (zs == null) return;

                    int found = 0, addedTotal = 0, uncreatedTotal = 0;
                    foreach (var zone in _heldZones)
                    {
                        // Only once that zone's terrain actually exists.
                        // Vanilla guards its own creation pass with
                        // IsActiveAreaLoaded(), but that only ever checks
                        // around the PLAYER - handing it ZDOs from a zone
                        // whose Heightmap isn't built yet produced
                        // "Terrain compiler could not find hmap" followed by
                        // a NullReferenceException in TerrainComp.Load, since
                        // a terrain-modifier object was instantiated with no
                        // heightmap to attach to.
                        if (!zs.IsZoneLoaded(zone)) continue;

                        // SingleZone, not the player's simulationDistance:
                        // gathering with the player's radius would pull ZDOs
                        // from the whole block around the leash, defeating
                        // the one-chunk limit and instantiating objects for
                        // zones that were never held open in the first place.
                        _buf.Clear();
                        __instance.FindSectorObjects(zone, SingleZone, _buf);

                        int added = 0, uncreated = 0;
                        foreach (var zdo in _buf)
                        {
                            if (zdo == null) continue;
                            if (!zdo.Created) uncreated++;
                            if (_seen.Add(zdo)) { sectorObjects.Add(zdo); added++; }
                        }

                        // Splits the remaining unknown cleanly. found=0 means
                        // ZDOMan isn't holding that sector's objects at all
                        // and the problem is upstream of this patch entirely.
                        // found>0 with uncreated>0, yet nothing ever spawns,
                        // means they ARE being offered and something
                        // downstream in ZNetScene is refusing them -
                        // IsZoneReadyForType, or the per-frame creation cap
                        // starving them since it builds nearest-first and
                        // these sort last at 3.5km.
                        found += _buf.Count; addedTotal += added; uncreatedTotal += uncreated;
                    }
                    _appendReport = $"found={found}, added={addedTotal}, notYetCreated={uncreatedTotal}";
                }
                finally { _reentry = false; }
            }
        }

        /// <summary>Marks ZNetScene's create/destroy pass so the sector
        /// append above applies to it and nothing else. Finalizer rather
        /// than postfix so an exception can't leave the flag stuck on.</summary>
        [HarmonyPatch(typeof(ZNetScene), "CreateDestroyObjects")]
        internal static class Patch_ZNetScene_CreateDestroyObjects_Scope
        {
            internal static bool Active;
            static void Prefix() => Active = true;
            static Exception Finalizer(Exception __exception) { Active = false; return __exception; }
        }

        /// <summary>Drops a remembered position - used when a leash is
        /// confirmed destroyed, so its area stops being held open.</summary>
        public static void Forget(Vector3 pos)
        {
            for (int i = _leashPositions.Count - 1; i >= 0; i--)
            {
                if ((_leashPositions[i] - pos).sqrMagnitude < 4f)
                {
                    _leashPositions.RemoveAt(i);
                    Save();
                    if (Plugin.Verbose)
                        Plugin.Log.LogInfo($"[keepalive] forgot leash at {pos}.");
                    return;
                }
            }
        }
    }
}
