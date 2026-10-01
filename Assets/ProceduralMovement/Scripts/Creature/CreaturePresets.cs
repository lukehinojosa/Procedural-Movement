using System.Collections.Generic;
using UnityEngine;

namespace ProceduralMovement
{
    public enum CreaturePreset { Spider, Human, Horse, Ant, Centipede }

    /// <summary>
    /// Built-in creature definitions. These are ordinary data, the same format the Creature Creator edits and saves.
    /// Coordinates are root-local: +X right, +Y up, +Z forward. The root sits bodyHeight above the ground at rest.
    /// </summary>
    public static class CreaturePresets
    {
        public static CreatureDefinition Get(CreaturePreset preset)
        {
            switch (preset)
            {
                case CreaturePreset.Human: return Human();
                case CreaturePreset.Horse: return Horse();
                case CreaturePreset.Ant: return Ant();
                case CreaturePreset.Centipede: return Centipede();
                default: return Spider();
            }
        }

        static Vector3 OutHint(Vector3 attach, Vector3 rest, float up)
        {
            Vector3 d = rest - attach; d.y = 0f;
            return (d.normalized + Vector3.up * up).normalized;
        }

        public static CreatureDefinition Spider()
        {
            var d = new CreatureDefinition
            {
                name = "Spider",
                palette = new[]
                {
                    new Color(0.13f, 0.11f, 0.1f), new Color(0.62f, 0.3f, 0.1f), new Color(0.9f, 0.52f, 0.14f),
                    new Color(0.04f, 0.035f, 0.03f), new Color(0.01f, 0.01f, 0.01f),
                },
                smoothness = 0.55f,
                bodyPattern = SkinPattern.Organic,
                bodyColor = 0,
            };
            d.body.Add(new BodyNode(new Vector3(0, 0.0f, -0.2f), 0.1f, 1f, 0.8f));
            d.body.Add(new BodyNode(new Vector3(0, 0.04f, -0.08f), 0.22f, 1.05f, 0.72f));
            d.body.Add(new BodyNode(new Vector3(0, 0.05f, 0.1f), 0.21f, 1f, 0.72f));
            d.body.Add(new BodyNode(new Vector3(0, 0.02f, 0.24f), 0.12f, 1f, 0.8f));
            d.head.enabled = false;

            d.tail = new TailDef
            {
                enabled = true, section = Section.Root, attach = new Vector3(0, 0.05f, -0.24f), direction = new Vector3(0, 0.35f, -1f),
                segments = 3, length = 0.85f, radii = new[] { 0.07f, 0.34f, 0.38f, 0.2f }, aspect = new Vector2(1f, 0.85f),
                stiffness = 0.85f, damping = 0.8f, gravity = 0.1f, wag = 0f, color = 1, pattern = SkinPattern.Bands,
            };

            float[] attachZ = { 0.17f, 0.07f, -0.03f, -0.13f };
            Vector2[] rest = { new Vector2(0.72f, 0.95f), new Vector2(1.02f, 0.38f), new Vector2(1.02f, -0.24f), new Vector2(0.74f, -0.8f) };
            for (int i = 0; i < 4; i++)
            {
                var attach = new Vector3(0.15f, 0.0f, attachZ[i]);
                var r = new Vector3(rest[i].x, 0, rest[i].y);
                d.limbs.Add(new LimbDef
                {
                    name = "Leg" + (i + 1), role = LimbRole.Leg, section = Section.Root, attach = attach,
                    bones = new[] { 0.55f, 0.55f, 0.5f }, radii = new[] { 0.06f, 0.05f, 0.035f, 0.012f },
                    hints = new[] { new Vector3(0.2f, 1f, 0f), OutHint(attach, r, 0.25f) },
                    rest = r, foot = FootType.Point, footHeight = 0.015f, color = 0, boneColors = new[] { 0, 0, 3 },
                });
            }
            d.limbs.Add(new LimbDef
            {
                name = "Palp", role = LimbRole.Arm, section = Section.Root, attach = new Vector3(0.06f, -0.02f, 0.27f),
                bones = new[] { 0.14f, 0.15f }, radii = new[] { 0.03f, 0.025f, 0.02f }, hints = new[] { new Vector3(0.3f, 0.8f, 0.3f) },
                rest = new Vector3(0.12f, -0.3f, 0.5f), foot = FootType.Point, footLength = 0f, color = 0, footColor = 0,
            });

            const float eyeSmooth = 0.95f;
            d.shapes.Add(new ShapeDef(ShapeKind.Ellipsoid, Section.Root, new Vector3(0.045f, 0.13f, 0.28f), Vector3.one * 0.035f, 4, true, default, eyeSmooth));
            d.shapes.Add(new ShapeDef(ShapeKind.Ellipsoid, Section.Root, new Vector3(0.1f, 0.12f, 0.25f), Vector3.one * 0.022f, 4, true, default, eyeSmooth));
            d.shapes.Add(new ShapeDef(ShapeKind.Ellipsoid, Section.Root, new Vector3(0.035f, 0.17f, 0.22f), Vector3.one * 0.02f, 4, true, default, eyeSmooth));
            d.shapes.Add(new ShapeDef(ShapeKind.Ellipsoid, Section.Root, new Vector3(0.085f, 0.16f, 0.17f), Vector3.one * 0.018f, 4, true, default, eyeSmooth));
            d.shapes.Add(new ShapeDef(ShapeKind.Capsule, Section.Root, new Vector3(0.045f, -0.02f, 0.3f), new Vector3(0.045f, 0.018f, 0.14f), 3, true, new Vector3(65f, 0, 0)));
            // Orange accent stripe on the carapace.
            d.shapes.Add(new ShapeDef(ShapeKind.Ellipsoid, Section.Root, new Vector3(0, 0.185f, 0.0f), new Vector3(0.05f, 0.02f, 0.16f), 2));

            var l = d.locomotion;
            l.bodyHeight = 0.5f; l.minSpeed = 0.3f; l.cruiseSpeed = 1.8f; l.maxSpeed = 6f; l.acceleration = 10f;
            l.turnSpeed = 420f; l.turnSpeedAtMax = 260f; l.canClimb = true; l.surfaceAlign = 1f; l.maxSlope = 90f;
            l.maxStepUp = 0.3f; l.collisionRadius = 0.35f; l.bodyHalfLength = 0.15f; l.tiltFromFeet = 0.6f;
            l.jumpSpeed = 5f; l.settleTolerance = 0.16f; l.startDelay = 0.08f;
            l.gaits = new List<GaitDef>
            {
                new GaitDef { name = "Crawl", pattern = GaitPattern.Alternating, minSpeed = 0f, duty = 0.6f, strideMin = 0.35f, strideMax = 1.0f, minFrequency = 1.3f, stepHeight = 0.22f, bob = 0.015f },
                new GaitDef { name = "Scuttle", pattern = GaitPattern.Alternating, minSpeed = 3f, duty = 0.45f, strideMin = 0.9f, strideMax = 1.5f, minFrequency = 2.4f, stepHeight = 0.28f, bob = -0.02f, crouch = 0.1f },
            };
            return d;
        }

        public static CreatureDefinition Human()
        {
            var d = new CreatureDefinition
            {
                name = "Human",
                palette = new[]
                {
                    new Color(0.2f, 0.42f, 0.72f), new Color(0.2f, 0.22f, 0.3f), new Color(0.86f, 0.66f, 0.53f),
                    new Color(0.12f, 0.09f, 0.07f), new Color(0.03f, 0.03f, 0.03f),
                },
                smoothness = 0.25f,
                bodyPattern = SkinPattern.Organic,
                bodyColor = 0,
                frontSectionStart = 2,
                frontPivot = new Vector3(0, 0.14f, 0),
            };
            d.body.Add(new BodyNode(new Vector3(0, -0.02f, 0f), 0.14f, 1.3f, 0.85f));
            d.body.Add(new BodyNode(new Vector3(0, 0.14f, 0.01f), 0.125f, 1.2f, 0.78f));
            d.body.Add(new BodyNode(new Vector3(0, 0.33f, 0.02f), 0.15f, 1.3f, 0.8f));
            d.body.Add(new BodyNode(new Vector3(0, 0.47f, 0.0f), 0.13f, 1.45f, 0.75f));

            d.head = new HeadDef
            {
                enabled = true, neckBase = new Vector3(0, 0.55f, 0f), headOffset = new Vector3(0, 0.19f, 0.02f),
                neckRadius = 0.055f, neckTipRadius = 0.05f, headRadii = new Vector3(0.095f, 0.115f, 0.105f),
                restPitch = 0f, lookAmount = 0.7f, bobAmount = 0f, color = 2,
            };
            d.shapes.Add(new ShapeDef(ShapeKind.Ellipsoid, Section.Root, new Vector3(0, -0.05f, 0), new Vector3(0.165f, 0.12f, 0.12f), 1));
            d.shapes.Add(new ShapeDef(ShapeKind.Ellipsoid, Section.Head, new Vector3(0, 0.03f, -0.018f), new Vector3(0.1f, 0.1f, 0.105f), 3));
            d.shapes.Add(new ShapeDef(ShapeKind.Ellipsoid, Section.Head, new Vector3(0.034f, 0.015f, 0.092f), Vector3.one * 0.014f, 4, true, default, 0.9f));
            d.shapes.Add(new ShapeDef(ShapeKind.Ellipsoid, Section.Head, new Vector3(0, -0.012f, 0.1f), new Vector3(0.014f, 0.02f, 0.02f), 2));
            d.shapes.Add(new ShapeDef(ShapeKind.Ellipsoid, Section.Head, new Vector3(0.095f, 0f, 0f), new Vector3(0.015f, 0.03f, 0.022f), 2, true));
            // Shirt collar and belt.
            d.shapes.Add(new ShapeDef(ShapeKind.Ellipsoid, Section.Front, new Vector3(0, 0.53f, 0f), new Vector3(0.09f, 0.035f, 0.08f), 0));
            d.shapes.Add(new ShapeDef(ShapeKind.Ellipsoid, Section.Root, new Vector3(0, 0.05f, 0.005f), new Vector3(0.158f, 0.03f, 0.105f), 3));

            d.limbs.Add(new LimbDef
            {
                name = "Leg", role = LimbRole.Leg, section = Section.Root, attach = new Vector3(0.095f, -0.06f, 0f),
                bones = new[] { 0.45f, 0.44f }, radii = new[] { 0.075f, 0.055f, 0.042f }, hints = new[] { new Vector3(0, 0, 1) },
                rest = new Vector3(0.1f, 0, 0.02f), foot = FootType.Plantigrade, footLength = 0.25f, footHeight = 0.08f,
                color = 1, boneColors = new[] { 1, 1 }, footColor = 3,
            });
            d.limbs.Add(new LimbDef
            {
                name = "Arm", role = LimbRole.Arm, section = Section.Front, attach = new Vector3(0.19f, 0.45f, 0f),
                bones = new[] { 0.29f, 0.26f }, radii = new[] { 0.05f, 0.04f, 0.032f }, hints = new[] { new Vector3(0.15f, 0, -1f) },
                rest = new Vector3(0.21f, -0.08f, 0.03f), foot = FootType.Point, footLength = 0.09f,
                color = 0, boneColors = new[] { 0, 2 }, footColor = 2,
            });

            var l = d.locomotion;
            l.bodyHeight = 1.02f; l.minSpeed = 0.4f; l.cruiseSpeed = 1.5f; l.maxSpeed = 7f; l.acceleration = 3.5f;
            l.turnSpeed = 600f; l.turnSpeedAtMax = 320f; l.canClimb = false; l.surfaceAlign = 0f; l.maxSlope = 48f;
            l.maxStepUp = 0.38f; l.collisionRadius = 0.28f; l.bodyHalfLength = 0f; l.tiltFromFeet = 0f;
            l.spineTwist = 10f; l.armSwing = 0.32f; l.jumpSpeed = 5f; l.settleTolerance = 0.1f; l.startDelay = 0.3f;
            l.gaits = new List<GaitDef>
            {
                new GaitDef { name = "Walk", pattern = GaitPattern.Biped, swing = SwingStyle.Walk, clearance = 0.017f, minSpeed = 0f, duty = 0.6f, strideMin = 0.7f, strideMax = 2.1f, minFrequency = 0.9f, froudeStride = 2.28f, froudeStrideExp = 0.25f, froudeDuty = 0.542f, froudeDutyExp = -0.063f, stepHeight = 0.12f, kneeFlex = 60f, kneeFlexFast = 65f, bob = 0.025f, sway = 0.03f, lean = 3f },
                new GaitDef { name = "Run", pattern = GaitPattern.Biped, swing = SwingStyle.Run, clearance = 0.017f, minSpeed = 2.1f, duty = 0.34f, strideMin = 1.6f, strideMax = 4.6f, minFrequency = 1.45f, froudeStride = 2.312f, froudeStrideExp = 0.39f, froudeDuty = 0.394f, froudeDutyExp = -0.174f, stepHeight = 0.18f, curl = 0.08f, kneeFlex = 77f, kneeFlexFast = 125f, bob = -0.015f, sway = 0.01f, lean = 12f, crouch = 0.06f },
            };
            return d;
        }

        public static CreatureDefinition Horse()
        {
            var d = new CreatureDefinition
            {
                name = "Horse",
                palette = new[]
                {
                    new Color(0.42f, 0.24f, 0.13f), new Color(0.3f, 0.17f, 0.1f), new Color(0.92f, 0.9f, 0.86f),
                    new Color(0.07f, 0.055f, 0.05f), new Color(0.02f, 0.02f, 0.02f),
                },
                smoothness = 0.45f,
                bodyPattern = SkinPattern.Organic,
                bodyColor = 0,
                frontSectionStart = 3,
                frontPivot = new Vector3(0, 0f, 0.25f),
            };
            d.body.Add(new BodyNode(new Vector3(0, 0.1f, -0.78f), 0.27f, 0.95f, 1f));
            d.body.Add(new BodyNode(new Vector3(0, 0.05f, -0.45f), 0.34f, 0.95f, 1.08f));
            d.body.Add(new BodyNode(new Vector3(0, 0f, 0.05f), 0.36f, 0.92f, 1.1f));
            d.body.Add(new BodyNode(new Vector3(0, 0.04f, 0.45f), 0.33f, 0.9f, 1.1f));
            d.body.Add(new BodyNode(new Vector3(0, 0.1f, 0.72f), 0.26f, 0.85f, 1.1f));

            d.head = new HeadDef
            {
                enabled = true, neckBase = new Vector3(0, 0.22f, 0.8f), headOffset = new Vector3(0, 0.52f, 0.42f),
                neckRadius = 0.21f, neckTipRadius = 0.12f, headRadii = new Vector3(0.11f, 0.14f, 0.15f),
                snoutLength = 0.38f, snoutRadius = 0.105f, snoutTipRadius = 0.085f,
                restPitch = 52f, lookAmount = 0.4f, bobAmount = 0.05f, color = 0, mane = true, maneColor = 3,
            };
            d.shapes.Add(new ShapeDef(ShapeKind.Capsule, Section.Head, new Vector3(0.05f, 0.1f, -0.03f), new Vector3(0.03f, 0.012f, 0.13f), 0, true, new Vector3(-128f, 8f, 0f)));
            d.shapes.Add(new ShapeDef(ShapeKind.Ellipsoid, Section.Head, new Vector3(0.085f, 0.035f, 0.03f), Vector3.one * 0.022f, 4, true, default, 0.9f));
            d.shapes.Add(new ShapeDef(ShapeKind.Ellipsoid, Section.Head, new Vector3(0, -0.005f, 0.37f), new Vector3(0.082f, 0.075f, 0.075f), 1));
            // White blaze down the face.
            d.shapes.Add(new ShapeDef(ShapeKind.Capsule, Section.Head, new Vector3(0, 0.075f, 0.02f), new Vector3(0.03f, 0.02f, 0.3f), 2, false, new Vector3(9f, 0, 0)));

            d.tail = new TailDef
            {
                enabled = true, section = Section.Root, attach = new Vector3(0, 0.2f, -0.98f), direction = new Vector3(0, -1f, -0.45f),
                segments = 6, length = 0.95f, radii = new[] { 0.07f, 0.09f, 0.1f, 0.1f, 0.09f, 0.07f, 0.035f }, aspect = new Vector2(0.75f, 1.2f),
                stiffness = 0.22f, damping = 0.9f, gravity = 1.4f, wag = 0.25f, color = 3,
            };

            d.limbs.Add(new LimbDef
            {
                name = "Fore", role = LimbRole.Leg, section = Section.Front, attach = new Vector3(0.14f, -0.1f, 0.6f),
                bones = new[] { 0.38f, 0.42f, 0.32f }, radii = new[] { 0.17f, 0.08f, 0.05f, 0.045f },
                hints = new[] { new Vector3(0, 0, -1f), new Vector3(0, 0, 1f) },
                rest = new Vector3(0.17f, 0, 0.64f), foot = FootType.Hoof, footLength = 0.1f, footHeight = 0.15f,
                color = 0, boneColors = new[] { 0, 0, 2 }, footColor = 3, girdleSwing = 0.15f,
            });
            d.limbs.Add(new LimbDef
            {
                name = "Hind", role = LimbRole.Leg, section = Section.Root, attach = new Vector3(0.14f, -0.03f, -0.62f),
                bones = new[] { 0.46f, 0.44f, 0.34f }, radii = new[] { 0.22f, 0.1f, 0.055f, 0.045f },
                hints = new[] { new Vector3(0, 0, 1f), new Vector3(0, 0, -1f) },
                rest = new Vector3(0.17f, 0, -0.7f), foot = FootType.Hoof, footLength = 0.1f, footHeight = 0.15f,
                color = 0, boneColors = new[] { 0, 0, 0 }, footColor = 3, girdleSwing = 0.06f,
            });

            var l = d.locomotion;
            l.bodyHeight = 1.3f; l.minSpeed = 0.8f; l.cruiseSpeed = 1.8f; l.maxSpeed = 12f; l.acceleration = 5f;
            l.turnSpeed = 220f; l.turnSpeedAtMax = 90f; l.canClimb = false; l.surfaceAlign = 0.6f; l.maxSlope = 40f;
            l.maxStepUp = 0.45f; l.collisionRadius = 0.5f; l.bodyHalfLength = 0.55f; l.tiltFromFeet = 0.8f;
            l.jumpSpeed = 6.5f; l.settleTolerance = 0.15f; l.startDelay = 0.2f;
            l.gaits = new List<GaitDef>
            {
                new GaitDef { name = "Walk", pattern = GaitPattern.QuadWalk, swing = SwingStyle.Walk, clearance = 0.017f, minSpeed = 0f, duty = 0.64f, strideMin = 0.8f, strideMax = 2.4f, minFrequency = 1.0f, froudeStride = 2.3f, froudeStrideExp = 0.3f, stepHeight = 0.16f, kneeFlex = 55f, kneeFlexFast = 62f, bob = 0.02f, pitchRock = 1f },
                // Trot limb flexion is the whole-limb equivalent, set so the fore hoof arc peaks at ~10% of leg length
                // (trotting horses: 13.8 +/- 3.8 cm, Brown et al. 2015).
                new GaitDef { name = "Trot", pattern = GaitPattern.QuadTrot, swing = SwingStyle.Walk, clearance = 0.017f, minSpeed = 2.4f, duty = 0.45f, strideMin = 1.6f, strideMax = 3.6f, minFrequency = 1.45f, froudeStride = 2.3f, froudeStrideExp = 0.3f, stepHeight = 0.22f, curl = 0.1f, kneeFlex = 60f, kneeFlexFast = 68f, bob = -0.035f, pitchRock = 1.8f },
                new GaitDef { name = "Gallop", pattern = GaitPattern.QuadGallop, swing = SwingStyle.Run, clearance = 0.017f, minSpeed = 6f, duty = 0.28f, strideMin = 2.8f, strideMax = 7f, minFrequency = 1.95f, froudeStride = 2.3f, froudeStrideExp = 0.3f, stepHeight = 0.3f, curl = 0.18f, kneeFlex = 88f, kneeFlexFast = 100f, bob = -0.05f, pitchRock = 5.6f, crouch = 0.04f },
            };
            return d;
        }

        public static CreatureDefinition Ant()
        {
            var d = new CreatureDefinition
            {
                name = "Ant",
                palette = new[]
                {
                    new Color(0.45f, 0.09f, 0.05f), new Color(0.32f, 0.06f, 0.04f), new Color(0.8f, 0.3f, 0.12f),
                    new Color(0.06f, 0.03f, 0.02f), new Color(0.01f, 0.01f, 0.01f),
                },
                smoothness = 0.7f,
                bodyPattern = SkinPattern.Plain,
            };
            d.body.Add(new BodyNode(new Vector3(0, 0f, -0.14f), 0.075f, 1f, 0.9f));
            d.body.Add(new BodyNode(new Vector3(0, 0.02f, 0f), 0.1f, 0.95f, 0.85f));
            d.body.Add(new BodyNode(new Vector3(0, 0.02f, 0.14f), 0.08f, 1f, 0.9f));
            d.head = new HeadDef
            {
                enabled = true, neckBase = new Vector3(0, 0.03f, 0.2f), headOffset = new Vector3(0, 0.05f, 0.13f),
                neckRadius = 0.04f, neckTipRadius = 0.04f, headRadii = new Vector3(0.1f, 0.08f, 0.1f), restPitch = 10f, lookAmount = 0.8f, color = 0,
            };
            d.shapes.Add(new ShapeDef(ShapeKind.Capsule, Section.Head, new Vector3(0.04f, 0.06f, 0.06f), new Vector3(0.012f, 0.008f, 0.26f), 3, true, new Vector3(-50f, 25f, 0)));
            d.shapes.Add(new ShapeDef(ShapeKind.Ellipsoid, Section.Head, new Vector3(0.075f, 0.02f, 0.045f), Vector3.one * 0.03f, 4, true, default, 0.95f));
            d.shapes.Add(new ShapeDef(ShapeKind.Capsule, Section.Head, new Vector3(0.04f, -0.04f, 0.09f), new Vector3(0.02f, 0.01f, 0.09f), 3, true, new Vector3(20f, -30f, 0)));
            d.tail = new TailDef
            {
                enabled = true, section = Section.Root, attach = new Vector3(0, 0.02f, -0.2f), direction = new Vector3(0, 0.15f, -1f),
                segments = 3, length = 0.5f, radii = new[] { 0.04f, 0.17f, 0.19f, 0.1f }, stiffness = 0.8f, damping = 0.8f, gravity = 0.2f, color = 1,
            };
            float[] z = { 0.1f, 0f, -0.1f };
            Vector2[] rest = { new Vector2(0.45f, 0.45f), new Vector2(0.55f, 0f), new Vector2(0.45f, -0.45f) };
            for (int i = 0; i < 3; i++)
            {
                var a = new Vector3(0.07f, 0, z[i]);
                var r = new Vector3(rest[i].x, 0, rest[i].y);
                d.limbs.Add(new LimbDef
                {
                    name = "Leg" + (i + 1), role = LimbRole.Leg, attach = a, bones = new[] { 0.3f, 0.36f }, radii = new[] { 0.035f, 0.025f, 0.012f },
                    hints = new[] { new Vector3(0.2f, 1f, 0f) }, rest = r, foot = FootType.Point, footHeight = 0.01f, color = 0,
                });
            }
            var l = d.locomotion;
            l.bodyHeight = 0.3f; l.minSpeed = 0.3f; l.cruiseSpeed = 1.5f; l.maxSpeed = 5f; l.acceleration = 12f;
            l.turnSpeed = 500f; l.turnSpeedAtMax = 300f; l.canClimb = true; l.surfaceAlign = 1f; l.maxSlope = 90f; l.maxStepUp = 0.2f;
            l.collisionRadius = 0.22f; l.bodyHalfLength = 0.1f; l.tiltFromFeet = 0.6f; l.settleTolerance = 0.1f;
            l.gaits = new List<GaitDef>
            {
                new GaitDef { name = "Tripod", pattern = GaitPattern.Alternating, duty = 0.55f, strideMin = 0.25f, strideMax = 0.8f, minFrequency = 1.8f, stepHeight = 0.12f, bob = 0.01f },
                new GaitDef { name = "Sprint", pattern = GaitPattern.Alternating, minSpeed = 2.6f, duty = 0.45f, strideMin = 0.7f, strideMax = 1.1f, minFrequency = 3f, stepHeight = 0.15f, bob = -0.01f },
            };
            return d;
        }

        public static CreatureDefinition Centipede()
        {
            var d = new CreatureDefinition
            {
                name = "Centipede",
                palette = new[]
                {
                    new Color(0.55f, 0.28f, 0.08f), new Color(0.2f, 0.1f, 0.05f), new Color(0.95f, 0.75f, 0.2f),
                    new Color(0.08f, 0.04f, 0.02f), new Color(0.01f, 0.01f, 0.01f),
                },
                smoothness = 0.6f,
                bodyPattern = SkinPattern.Bands,
            };
            int pairs = 9;
            float len = 1.8f;
            for (int i = 0; i <= pairs; i++)
            {
                float z = len * 0.5f - len * i / pairs;
                d.body.Add(new BodyNode(new Vector3(0, 0, z), 0.085f, 1.3f, 0.7f));
            }
            d.head = new HeadDef
            {
                enabled = true, neckBase = new Vector3(0, 0, len * 0.5f), headOffset = new Vector3(0, 0.02f, 0.12f),
                neckRadius = 0.07f, neckTipRadius = 0.06f, headRadii = new Vector3(0.1f, 0.06f, 0.1f), lookAmount = 0.9f, color = 1,
            };
            d.shapes.Add(new ShapeDef(ShapeKind.Capsule, Section.Head, new Vector3(0.04f, 0.02f, 0.07f), new Vector3(0.012f, 0.006f, 0.3f), 2, true, new Vector3(-15f, 30f, 0)));
            d.shapes.Add(new ShapeDef(ShapeKind.Capsule, Section.Head, new Vector3(0.05f, -0.03f, 0.07f), new Vector3(0.02f, 0.008f, 0.1f), 3, true, new Vector3(10f, -40f, 0)));
            d.tail = new TailDef
            {
                enabled = true, attach = new Vector3(0, 0, -len * 0.5f), direction = new Vector3(0, 0.1f, -1f), segments = 3, length = 0.3f,
                radii = new[] { 0.07f, 0.05f, 0.03f, 0.01f }, stiffness = 0.4f, gravity = 0.2f, color = 0,
            };
            for (int i = 0; i < pairs; i++)
            {
                float z = len * 0.5f - len * (i + 0.5f) / pairs;
                var a = new Vector3(0.09f, 0, z);
                d.limbs.Add(new LimbDef
                {
                    name = "Leg" + (i + 1), role = LimbRole.Leg, attach = a, bones = new[] { 0.16f, 0.2f }, radii = new[] { 0.025f, 0.018f, 0.008f },
                    hints = new[] { new Vector3(0.3f, 1f, 0f) }, rest = new Vector3(0.34f, 0, z + 0.04f), foot = FootType.Point, footHeight = 0.008f, color = 2,
                });
            }
            var l = d.locomotion;
            l.bodyHeight = 0.16f; l.minSpeed = 0.3f; l.cruiseSpeed = 1.2f; l.maxSpeed = 4f; l.acceleration = 10f;
            l.turnSpeed = 260f; l.turnSpeedAtMax = 160f; l.canClimb = true; l.surfaceAlign = 1f; l.maxSlope = 90f; l.maxStepUp = 0.12f;
            l.collisionRadius = 0.16f; l.bodyHalfLength = 0.7f; l.tiltFromFeet = 0.4f; l.settleTolerance = 0.06f;
            l.gaits = new List<GaitDef>
            {
                new GaitDef { name = "Ripple", pattern = GaitPattern.Wave, duty = 0.6f, strideMin = 0.15f, strideMax = 0.4f, minFrequency = 2f, stepHeight = 0.07f, bob = 0f },
            };
            return d;
        }
    }
}
