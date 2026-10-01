using System.Collections.Generic;
using UnityEngine;

namespace ProceduralMovement
{
    /// <summary>
    /// Entry point of a demo scene: builds the city, sets up lighting and the camera, spawns the player
    /// creature (a preset, or the custom creature from the creator) plus wandering background creatures.
    /// </summary>
    public class DemoBootstrap : MonoBehaviour
    {
        public CreaturePreset startCreature = CreaturePreset.Spider;
        public bool spawnWanderers = true;
        public int wanderersPerType = 2;
        public int citySeed = 20260927;

        public Creature Player { get; private set; }
        public ThirdPersonCamera CameraRig { get; private set; }
        public PlayerCreatureController Controller { get; private set; }
        public CityGenerator City { get; private set; }
        public bool IsCustom { get; private set; }
        readonly List<Creature> m_Wanderers = new List<Creature>();
        static readonly Color[] s_SkinTones =
        {
            new Color(0.96f, 0.8f, 0.69f), new Color(0.87f, 0.66f, 0.53f), new Color(0.72f, 0.51f, 0.37f),
            new Color(0.55f, 0.37f, 0.26f), new Color(0.38f, 0.25f, 0.18f),
        };
        static readonly Color[] s_HairTones =
        {
            new Color(0.08f, 0.06f, 0.05f), new Color(0.3f, 0.19f, 0.1f), new Color(0.75f, 0.6f, 0.35f), new Color(0.5f, 0.2f, 0.1f), new Color(0.6f, 0.6f, 0.6f),
        };
        Transform m_CreatureRoot;

        void Start()
        {
            Application.targetFrameRate = -1;
            var cam = WorldSetup.ConfigureCamera();
            WorldSetup.ConfigureLighting();

            City = new CityGenerator();
            City.Generate(citySeed);
            Physics.SyncTransforms();

            m_CreatureRoot = new GameObject("Creatures").transform;
            CameraRig = cam.GetComponent<ThirdPersonCamera>();
            if (CameraRig == null) CameraRig = cam.gameObject.AddComponent<ThirdPersonCamera>();
            cam.gameObject.AddComponent<DebugOverlay>();
            Controller = gameObject.AddComponent<PlayerCreatureController>();
            Controller.viewCamera = cam;

            if (CreatureSession.UseCustomOnNextLoad && CreatureSession.Custom != null)
            {
                CreatureSession.UseCustomOnNextLoad = false;
                SpawnPlayer(CreatureSession.Custom.Clone(), true, City.PlayerSpawn, City.PlayerForward);
            }
            else SpawnPlayer(CreaturePresets.Get(startCreature), false, City.PlayerSpawn, City.PlayerForward);

            if (spawnWanderers) SetWanderers(true);
            gameObject.AddComponent<DemoHUD>().bootstrap = this;
        }

        public void SpawnPlayer(CreatureDefinition def, bool custom, Vector3 pos, Vector3 fwd)
        {
            if (Player != null) Destroy(Player.gameObject);
            Player = Creature.Spawn(def, pos + Vector3.up * 0.3f, fwd, m_CreatureRoot);
            IsCustom = custom;
            Controller.creature = Player;
            CameraRig.SetTarget(Player, true);
            var overlay = CameraRig.GetComponent<DebugOverlay>();
            if (overlay != null) overlay.target = Player;
            if (Autopilot) SetAutopilot(true);
        }

        public bool Autopilot { get; private set; }

        /// <summary>Lets the player creature wander on its own (hands-free showcase).</summary>
        public void SetAutopilot(bool on)
        {
            Autopilot = on;
            Controller.enabled = !on;
            if (Player == null) return;
            var ai = Player.GetComponent<WanderAI>();
            if (on && ai == null)
            {
                ai = Player.gameObject.AddComponent<WanderAI>();
                ai.creature = Player;
                ai.center = City.PlayerSpawn;
                ai.radius = 30f;
            }
            else if (!on && ai != null) Destroy(ai);
        }

        /// <summary>Swaps the controlled creature in place, keeping position and heading.</summary>
        public void SwitchTo(CreatureDefinition def, bool custom)
        {
            Vector3 pos = Player != null ? Player.transform.position : City.PlayerSpawn;
            Vector3 fwd = Player != null ? Player.transform.forward : City.PlayerForward;
            // Climbers may be on a wall: respawn standing upright on the ground below.
            if (Player != null && Vector3.Dot(Player.Up, Vector3.up) < 0.8f)
            {
                if (Physics.Raycast(pos + Vector3.up * 0.5f, Vector3.down, out var hit, 200f, ~(1 << Creature.CreatureLayer), QueryTriggerInteraction.Ignore)) pos = hit.point;
                fwd = Vector3.ProjectOnPlane(fwd, Vector3.up).sqrMagnitude > 0.01f ? Vector3.ProjectOnPlane(fwd, Vector3.up) : Vector3.forward;
            }
            SpawnPlayer(def, custom, pos, fwd);
        }

        public void ResetToSpawn()
        {
            if (Player == null) return;
            Player.Teleport(City.PlayerSpawn + Vector3.up * 0.3f, City.PlayerForward);
            CameraRig.SetTarget(Player, true);
        }

        public bool WanderersActive => m_Wanderers.Count > 0;

        public void SetWanderers(bool on)
        {
            foreach (var w in m_Wanderers) if (w != null) Destroy(w.gameObject);
            m_Wanderers.Clear();
            if (!on) return;
            var types = new[] { CreaturePreset.Spider, CreaturePreset.Human, CreaturePreset.Horse };
            int spawn = 0;
            foreach (var t in types)
            {
                for (int i = 0; i < wanderersPerType; i++)
                {
                    var pts = City.spawnPoints;
                    Vector3 p = pts[spawn++ % pts.Count] + new Vector3(i * 3f, 0.3f, i * 2f);
                    var def = CreaturePresets.Get(t);
                    def.name = t + " (NPC)";
                    // Vary NPCs so they read as different individuals.
                    if (t == CreaturePreset.Human)
                    {
                        def.palette[0] = Random.ColorHSV(0f, 1f, 0.35f, 0.75f, 0.35f, 0.85f);
                        def.palette[1] = Random.ColorHSV(0f, 1f, 0.1f, 0.4f, 0.15f, 0.45f);
                        def.palette[2] = s_SkinTones[Random.Range(0, s_SkinTones.Length)];
                        def.palette[3] = s_HairTones[Random.Range(0, s_HairTones.Length)];
                    }
                    else
                    {
                        for (int k = 0; k < def.palette.Length - 1; k++)
                            def.palette[k] = Color.Lerp(def.palette[k], Random.ColorHSV(0f, 1f, 0.2f, 0.6f, 0.3f, 0.8f), 0.15f);
                    }
                    var c = Creature.Spawn(def, p, Quaternion.Euler(0, Random.Range(0, 360f), 0) * Vector3.forward, m_CreatureRoot);
                    var ai = c.gameObject.AddComponent<WanderAI>();
                    ai.creature = c;
                    ai.center = p;
                    ai.radius = 35f;
                    m_Wanderers.Add(c);
                }
            }
        }
    }
}
