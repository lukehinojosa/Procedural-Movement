# Procedural Movement

Data-driven procedural locomotion for arbitrary creatures, written from scratch (no animation clips, no IK or
locomotion packages, no imported models). Every mesh, texture and animation is generated at runtime.

## Scenes

| Scene | What it is |
|---|---|
| `Scenes/SpiderDemo` | City demo starting as the spider (climbs walls and ceilings) |
| `Scenes/HumanDemo` | City demo starting as the human (walk / run, stairs, curbs) |
| `Scenes/HorseDemo` | City demo starting as the horse (walk / trot / gallop) |
| `Scenes/CreatureCreator` | Build a creature, watch it walk, save it, take it into the city |

Rebuild the scenes any time with **Procedural Movement > Build Demo Scenes**.

## Controls

WASD / left stick move (camera relative, also on walls), Shift sprint, Ctrl creep, Q / E cruise speed,
Space jump (hold to crouch deeper and jump higher), F strafe, mouse / right stick orbit, wheel zoom, C camera mode (Orbit, Surface, Chase),
V camera position preset, Tab camera settings, 1 to 6 switch creature, G debug overlay, T slow motion,
P autopilot, R reset, H help.

## How it works

Per frame, `Creature.Update` runs:

1. **SurfaceMotor**: a kinematic capsule. Walkers stay upright, step up curbs and stairs, slide on walls and
   fall off ledges. Climbers fit a surface plane from a fan of probes, so they wrap floor to wall, over roof
   edges and onto ceilings.
2. **GaitController**: a phase clock. Stride and cadence come from speed, capped by how far the legs can reach.
   Each leg lifts when the clock passes its phase offset (tripod, wave, biped, walk, trot, gallop, bound).
   Gaits switch by speed with hysteresis.
3. **Foot planting** (`LimbRig`): stance feet stay locked in the world. Swinging feet fly on an arc (with a
   heel kick for fast gaits) to a predicted foothold, found by raycasts on any surface.
4. **Body pose**: height and tilt fit a plane through the footholds; bob, sway and fore/aft rocking come from
   the stance feet; the body dips slightly if a planted leg would over-extend; leans into acceleration and turns.
5. **IK** (`IKSolvers.cs`): analytic two-bone IK, and FABRIK with a bend hint per joint for longer legs
   (a horse hind leg bends forward at the stifle and backward at the hock).
6. **Arms, head, tail**: counter-swinging arms, a stabilised look-at head with a bobbing neck, verlet tails.
7. **Skin**: torsos, necks, manes and tails are smooth tubes rebuilt from moving control points.

## Reference data used for tuning

- Swing trajectory (`GaitDef.swing`): human and horse feet follow measured sagittal joint-angle curves instead
  of a lift arc. The hip-to-ankle chord of the leg points at (thigh angle - knee flexion / 2) from vertical and is
  cos(knee flexion / 2) of full length, so foot height scales with limb length and comes from the knee and hip:
  - Walking: knee ~38 deg at toe-off, peak ~60 deg a third into swing, straight before heel contact; hip flexes
    to its maximum in terminal swing. Running (Novacheck 1998): knee peaks ~90 deg near mid-swing (105 to 130
    deg sprinting), the hip flexes late then extends before contact (swing-leg retraction), knee ~20 deg at contact.
  - Timing follows from the same curves: the foot barely advances while the knee folds, swings through fast,
    and slows (slightly retracting) before contact, landing with ~0.3 of its average swing speed
    (heel-contact velocity ~0.9 m/s horizontal, ~0 vertical, Winter 1992).
  - Minimum toe clearance at mid-swing: ~1.6 cm (1.7% of leg length, Winter 1992; Wu and Kuo 2016), used as a floor
    for the lowest point of the angled foot (`GaitDef.clearance`).
  - Horse: fore hoof arc peak ~13.8 cm at the trot (~10% of leg length, Brown et al. 2015).
- Body weight and vertical motion: for data-driven gaits the body rides on its stance legs. Each supporting leg
  holds the hip where its knee has the measured stance flexion (walking ~5 deg at heel strike, ~18 deg in the
  loading response, straight again by mid-stance; running ~20 deg at contact to ~45 deg at mid-stance, Novacheck
  1998), so the body vaults over a walking leg (hip rises and falls ~4.8 cm per step; pelvis data ~4 to 5 cm) and
  a running leg compresses like a spring (centre of mass sinks ~5.5% of leg length into mid-stance at jogging
  speed, less when sprinting; ~6.5 cm peak to peak at 3 m/s). A horse's fetlock sinks under load (~20 deg
  beyond standing at the walk, ~35 deg at the trot and faster).
- Stance weight hand-over: a new foot takes load over its loading response while the trailing foot unloads; in
  the second half of its swing a leg already shares in holding the body from its coming foothold (the stance
  knee yields to lower the body onto a lower step or pushes it up to a higher one). No loaded leg is ever asked
  to stretch past its reach. Standing, the feet stay flat with a relaxed ~9 deg knee.
- Slopes (measured continuously along the direction of travel, stairs count as their average slope):
  - Stride: longer and slower uphill, shorter and quicker downhill at the same speed (~1% per degree;
    Kawamura 1991, Leroux 2002, Lay 2006). Reach limits account for the ground rising or falling ahead.
  - Uphill: more hip and knee flexion in swing (the knee lifts higher), the knee meets the ground more flexed
    and extends through stance to push the body up; the trunk leans forward (~0.6 deg per degree).
  - Downhill: the stance knee keeps bending through stance to brake and lower the body, heel-off is delayed,
    the swing is flatter, and the trunk leans back (Lay 2006; Leroux 2002; MacDonald 2022).
  - These gait changes come from studies of people and apply to two-legged walkers; other body plans get the
    physical effects (reach on sloped ground, settling onto the next foothold).
- Turning: to follow a curve the ground force must pass through the centre of mass while pushing it toward
  the centre of the turn, so feet land out wide of the hips by hip height x (sideways acceleration / g) and
  the body leans in by atan(sideways acceleration / g) (both slightly under the full value). Only sustained
  turning counts: brief steering bursts are smoothed (~0.35 s) and gentle curves (under ~0.6 m/s^2) keep the
  normal step width, as people spread a small change of heading over a step or two. The hips swing round to
  the new heading ahead of the body's momentum, and the feet sweep round on the outside.
- Jumping is a countermovement jump: holding jump crouches the body as the legs unload (it falls at about half
  gravity, braking into the bottom, so gravity sets how fast it gets there; ~0.5 s to the deepest point), to at
  most ~38% of hip height (shallower at a run), with the trunk folding forward. A tap dips ~11 cm. On release
  the legs extend with constant force: take-off speed grows with the square root of crouch depth (a
  mid-depth crouch gives the creature's jump speed, the deepest ~1.3x). Horizontal momentum is kept.
- IK near full extension is softened over the last 0.4% of reach (2% for chains of three or more bones), so a
  nearly straight knee cannot shake or snap. FABRIK runs a fixed number of iterations and warm-starts from the
  previous frame's pose, so near-straight chains stay on one solution instead of flicking between two.
- The body is held up by the legs under it: a planted foot more than about half a leg-length out from its hip
  (mid-turn, reversing) hands its share of the body to the others instead of dragging the body toward the ground.
- Swing knee flexion (`GaitDef.kneeFlex`): human ~60 deg walking, ~90 deg running, up to ~125 deg sprinting;
  horse values are whole-limb equivalents (carpus ~76 deg and fetlock ~62 to 77 deg in swing).
- Foot strike by speed (`LocomotionDef.footStrike*`): shod walking lands on the heel (~20 deg toe up),
  jogging on the midfoot / forefoot with the heel settling after, sprinting on the forefoot with the heel kept up.
  In swing the foot hangs square to the shin (plus relaxing push-off plantarflexion), then sets up the strike.
- Toe-off: planted feet roll onto the toe (heel rise up to ~55 deg; hoof breakover ~40 deg) and lift off when
  even that cannot keep the leg in reach, so legs are never dragged straight.
- Horse gallop: transverse sequence (trailing hind, leading hind, trailing fore, leading fore) in couplets;
  2.0 to 2.1 strides/s and duty 0.26 to 0.31 at 9.5 to 12.5 m/s (Thoroughbred data).
- Joint planes: each knee/elbow/hock bends about a hinge axis fixed from the rest pose (lateral for upright
  legs), so knees never drift sideways; sprawled arthropod legs turn their hinge with the foot like a coxa.
- Gait initiation: a short weight shift over the stance foot (the body barely moves, ~0.1 m/s at heel-off), then
  one leg steps first with a slower, shorter first step and the rest follow in order; walking gaits never lift a
  foot unless enough others are planted.
- Walk-to-run transition at ~2.1 m/s (Froude number ~0.5). Gait switches re-sync the clock to the legs'
  current footfalls.
- Stride and duty scale with leg length through the Froude number Fr = v^2 / (g h), h = hip height
  (`GaitDef.froudeStride`, `froudeDuty`):
  - Human walk: stride / h = 2.28 Fr^0.25, fitted to the constant walk ratio (step length / cadence
    ~0.0065 m per step/min, Sekiya). About 1.5 m strides at ~116 steps/min at 1.5 m/s and 86 steps/min at 0.8 m/s.
    Duty 0.542 Fr^-0.063 (stance ~64% slow to ~57% fast).
  - Human run: stride / h = 2.312 Fr^0.39, duty 0.394 Fr^-0.174 (dynamic-similarity fits for running,
    Donelan and Kram). ~165 steps/min at 3 m/s, ~175 at 4 m/s.
  - Horse (all gaits): stride / h = 2.3 Fr^0.3 (Alexander and Jayes, mammals). Walk ~0.95 Hz at 1.8 m/s,
    trot ~1.45 Hz at 4 m/s, gallop ~2.2 Hz at 11 m/s.
- The human cruise speed (1.5 m/s, Fr ~0.24) sits in the preferred walking range of healthy adults
  (~1.2 to 1.6 m/s); running starts at Fr ~0.5.
- Stride caps come from each leg's real reach: the hip height at the ends of stance (gait crouch, running bob),
  the foot's actual geometry (a heel rise pivots on the toe and lifts the ankle; hoof breakover), girdle swing
  (`LimbDef.girdleSwing`: a horse's scapula slides the shoulder ~15 cm, the pelvis ~6 cm) and where each foot
  rests relative to its hip. Running posture follows the spring-mass model: the hip is at ~95% of standing
  height at touch-down and lowest at mid-stance.
- Horse gallop trunk pitch: 11 deg rising to 19 deg peak-to-peak with speed, phase-locked to the footfalls;
  turns lean by atan(v * omega / g).
- Running leap: one-foot take-off with an almost straight take-off leg (long-jump data: hip ~163, knee ~172 deg),
  free-leg knee drive, landing on the lead leg and running on.

## Adding creatures

A creature is a `CreatureDefinition` (plain serialisable data: body nodes, limbs, head, tail, shapes,
palette, gaits). See `CreaturePresets.cs` for the spider, human, horse, ant and centipede. The creator edits a
`CreatorModel` that derives a definition from a template (leg pairs and joints per leg can change), and saves
JSON to `Application.persistentDataPath/Creatures`.

## Folders

`Scripts/Core` meshes and materials · `Scripts/IK` solvers · `Scripts/Locomotion` motor, gait, limbs ·
`Scripts/Creature` data, presets, rig · `Scripts/Control` camera and controllers · `Scripts/Environment`
city · `Scripts/Demo`, `Scripts/UI`, `Scripts/Creator` scenes and UI · `Editor` scene builder.
