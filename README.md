# Ursitoare Mirror

[Mirror](https://github.com/MirrorNetworking/Mirror) bindings for [Ursitoare](https://github.com/iFlex/Ursitoare), a client-side prediction and server reconciliation library for Rigidbody physics in Unity.

You add one object to your online scene and derive your networked physics behaviours from `AbstractPredictedNetworkBehaviour`. That gives them client-side prediction, server reconciliation and smoothed visuals.

**How it works.** The server is authoritative. Every physics tick, the client that controls an object samples its input, sends it to the server and simulates the tick right away. The server applies the same input and sends back the resulting state, tagged with the client's tick number. If the client's prediction for that tick was wrong, the client rewinds to it and replays its stored inputs up to the present. Objects the client doesn't control (other players, props) are simulated forward from the latest server state.

## Contents

1. [Requirements](#1-requirements)
2. [Install](#2-install)
3. [Scene setup](#3-scene-setup)
4. [The rule: all movement goes in `ApplyForces`](#4-the-rule-all-movement-goes-in-applyforces)
5. [Write a predicted behaviour](#5-write-a-predicted-behaviour)
6. [Set up the prefab](#6-set-up-the-prefab)
7. [Spawning and ownership](#7-spawning-and-ownership)
8. [Tuning](#8-tuning)
9. [Debugging](#9-debugging)
10. [Known limitations](#10-known-limitations)

## 1. Requirements

- Unity 6000.0 or newer (tested on 6000.3.7f1).
- Mirror 96 or newer (tested on 96.0.1), imported from the Asset Store or from a GitHub release `.unitypackage`.
- 3D physics (`Rigidbody`).

## 2. Install

Add all three packages to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "sector0.safe-event-dispatcher": "https://github.com/iFlex/SafeEventDispatcher.git",
    "sector0.ursitoare": "https://github.com/iFlex/Ursitoare.git",
    "sector0.ursitoare-mirror": "https://github.com/iFlex/Ursitoare-Mirror.git"
  }
}
```

You can also add them in **Package Manager › + › Install package from git URL**, in the order above. You need all three: the packages depend on each other, but Unity can't fetch git dependencies by itself. To pin a version, append `#<commit or tag>` to a URL.

The assemblies are auto-referenced, so scripts in `Assembly-CSharp` can use them straight away. If your code has its own `.asmdef`, reference `sector0.ursitoare`, `sector0.ursitoare-mirror`, `sector0.SafeEventDispatcher` and `Mirror`.

## 3. Scene setup

Your online scene needs two objects:

1. **A Mirror `NetworkManager`** with a transport, set up as usual. Your predicted prefabs go in **Player Prefab** and/or **Registered Spawnable Prefabs**.
2. **The prediction adapter.** Create an empty GameObject (for example `NetworkPredictionManager`) and add:
   - `NetworkIdentity`
   - `NetworkPredictionManagerAdapter`

   This object controls the physics simulation, applies inputs and does reconciliation. Make sure this object is spawned before any predicted object. The simplest thing is to leave it in the gameplay scene. Use exactly one, and keep it off the `NetworkManager`'s GameObject.

In `Awake`, the adapter applies its rate settings:

- Sets `Time.fixedDeltaTime` to 1 / Simulation Rate. This replaces the Fixed Timestep in Project Settings.
- Sets the `NetworkManager`'s Send Rate to Network Send Rate.
- Raises `Application.targetFrameRate` to Network Send Rate if it is capped below it. Mirror sends at most once per frame, so a lower frame rate would also lower the send rate.

When the server or client starts, the adapter does three things:

- Creates Ursitoare's prediction manager and wires it to Mirror. Inputs and states travel on the unreliable channel, ownership changes on the reliable one.
- Ticks the manager every `FixedUpdate`.
- Switches Unity physics to script mode, because Ursitoare calls `Physics.Simulate` itself.

The adapter's Inspector fields:

| Field | Default | Meaning |
|---|---|---|
| Simulation Rate | 60 | Physics ticks per second. |
| Network Send Rate | 30 | How many times per second Mirror sends its batched messages. |
| Physics History Buffer Size | 120 | Ticks of physics state kept for rewinds. Keep each predicted behaviour's Buffer Size at or below it. |

These values are applied only in `Awake` and aren't synced over the network, so the client and the server must use the same ones. Building both from the same scene takes care of that.

## 4. The rule: all movement goes in `ApplyForces`

> **Every change to a predicted object's motion must happen inside `ApplyForces()`, and nowhere else.** That rules out `Update`, `FixedUpdate`, `LateUpdate`, coroutines, `OnCollision*`/`OnTrigger*` callbacks, Mirror Commands, RPCs and SyncVar hooks, animation events and other scripts.

"Motion" means `AddForce`, `AddTorque`, setting `linearVelocity` or `angularVelocity`, `MovePosition`/`MoveRotation`, and writing `transform.position` or `transform.rotation`.

**Why:** `ApplyForces()` is the only gameplay code that Ursitoare runs on both the client and the server for the same tick, and the only code it re-runs when it rewinds and replays. Movement anywhere else happens once, on one machine, at the wrong tick. The server's result then never matches the client's prediction, and the client keeps correcting: it rewinds, replays and snaps, often every tick.

The rest of the contract:

- **Read input only in `SampleInput()`** and write it into the record. Only read input there; don't change any state.
- **`LoadInput()` copies the record into fields, and `ApplyForces()` reads only those fields.** It must never read `Input.*` or other live values, because during a replay `LoadInput()` feeds it input from the past.
- **Read values in the order you wrote them.** `LoadInput()` must read in the same order `SampleInput()` wrote. The counts must match `GetFloatInputCount()` and `GetBinaryInputCount()`.
- **Count time in ticks, not with `Time.time`.** During a replay, `Time.time` is the current time, not the time of the tick being replayed.
- **Any state that `ApplyForces()` reads or changes is component state.** This covers cooldowns, timers, fuel and similar values. Save it in `SampleComponentState()` and restore it in `LoadComponentState()`, so a rewind restores it together with the body.
- **Never call `Physics.Simulate` or change `Physics.simulationMode`.**
- **The client and the server must simulate the same thing:** same prefab, mass, colliders and physics settings.

Visual-only work can go anywhere: cameras, UI, audio, particles, animation.

## 5. Write a predicted behaviour

Derive from `AbstractPredictedNetworkBehaviour`, which is a Mirror `NetworkBehaviour`, and implement its abstract methods. This example moves with WASD and jumps with a cooldown:

```csharp
using Sector0.Ursitoare.Data;
using Sector0.UrsitoareMirror;
using UnityEngine;
using UnityEngine.InputSystem;

public class PredictedPlayer : AbstractPredictedNetworkBehaviour
{
    [SerializeField] float moveForce = 20f;
    [SerializeField] float jumpSpeed = 6f;
    [SerializeField] int jumpCooldownTicks = 30;

    // Input for the tick being simulated. LoadInput writes it, ApplyForces reads it.
    Vector2 move;
    bool jump;

    // State carried from tick to tick. Saved as component state so a rewind restores it.
    int cooldown;

    // ---------- Input ----------
    public override int GetFloatInputCount() => 2;
    public override int GetBinaryInputCount() => 1;

    public override void SampleInput(PredictionInputRecord record)
    {
        Vector2 m = Vector2.zero;
        bool j = false;
        Keyboard kb = Keyboard.current; // null on a dedicated server
        if (kb != null)
        {
            m.x = (kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0);
            m.y = (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0);
            j = kb.spaceKey.isPressed;
        }
        record.WriteNextScalar(m.x);
        record.WriteNextScalar(m.y);
        record.WriteNextBinary(j);
    }

    public override void LoadInput(PredictionInputRecord record)
    {
        move.x = record.ReadNextScalar(); // same order as SampleInput
        move.y = record.ReadNextScalar();
        jump = record.ReadNextBool();
    }

    // Runs on the server. Return false to drop input that can't be legitimate.
    public override bool ValidateInput(float deltaTime, PredictionInputRecord record) => true;

    public override void ClearInput()
    {
        move = Vector2.zero;
        jump = false;
    }

    // ---------- Simulation: the ONLY place that moves the body ----------
    public override void ApplyForces()
    {
        Rigidbody body = GetRigidbody();
        body.AddForce(new Vector3(move.x, 0, move.y) * moveForce);

        if (cooldown > 0)
            cooldown--;
        if (jump && cooldown == 0)
        {
            body.AddForce(Vector3.up * jumpSpeed, ForceMode.VelocityChange);
            cooldown = jumpCooldownTicks;
        }
    }

    // ---------- Component state ----------
    public override bool HasState() => true;
    public override int GetStateFloatCount() => 1;
    public override int GetStateBoolCount() => 0;

    public override void SampleComponentState(PhysicsStateRecord state)
    {
        state.componentState.WriteNextScalar(cooldown);
    }

    public override void LoadComponentState(PhysicsStateRecord state)
    {
        cooldown = Mathf.RoundToInt(state.componentState.ReadNextScalar());
    }
}
```

Notes:

- **No input and no custom forces?** For balls, crates and other props, skip the code and use the built-in `PredictedNonControllableBehaviour`.
- **Logic split across several components.** Other MonoBehaviours on the object can take part in prediction. If a component applies forces or carries state, it implements `PredictableComponent`; if it reads input, it implements `PredictableControllableComponent`; if it does both, it implements both. Add these components to the behaviour's **Prediction Components** list. They run in list order, before the behaviour itself, and the rules in section 4 apply to them too.
- **Overriding Mirror callbacks.** If you override `OnStartServer`, `OnStartClient`, `OnStopServer` or `OnStopClient`, call the base method. The same goes for `base.Awake()` if you add an `Awake`.

## 6. Set up the prefab

```
MyPredictedPlayer               (root)
│  NetworkIdentity
│  Rigidbody                    Interpolate: None
│  Collider(s)
│  PredictedPlayer              your AbstractPredictedNetworkBehaviour subclass
│  PredictedEntityVisuals
└─ Visuals                      renderers, camera, effects; no colliders
```

Wire it up:

| Component | Field | Set to |
|---|---|---|
| Your behaviour | Rigidbody | The root's `Rigidbody`. It's filled in automatically if left empty. It must be on the same GameObject as the behaviour. |
| | Visuals | The `PredictedEntityVisuals` component. **Required.** |
| | Prediction Components | Optional extra prediction components (see section 5). |
| | Buffer Size | Ticks of history to keep (see [Tuning](#8-tuning)). |
| `PredictedEntityVisuals` | Visuals Entity | The `Visuals` child. |
| | Server / Client Ghost Prefab | Optional debug markers (see [Debugging](#9-debugging)). |
| `NetworkManager` | Player Prefab or Registered Spawnable Prefabs | This prefab. |

Notes:

- **The `Visuals` child is detached at spawn.** An interpolator then moves it every frame, smoothing between physics ticks and hiding corrections. Put everything that should look smooth under it (meshes, the player's camera, effects), and never move it yourself. On despawn it is re-parented to its original place.
- **Keep colliders on the physics side, not under `Visuals`.** A detached collider doesn't follow the body.
- **Set the Rigidbody's Interpolate to None.** Ursitoare already smooths the visuals.
- **Don't add `NetworkTransform`, `NetworkRigidbody` or Mirror's `PredictedRigidbody`.** Ursitoare already syncs the body, and two sync systems would fight each other.

## 7. Spawning and ownership

An object's **owner** is the machine that samples its input and predicts it. Every other machine treats it as a follower.

### At spawn

At spawn, the owner is taken from Mirror:

- **Owned by a client:** the player object, and anything spawned with `NetworkServer.Spawn(obj, conn)`.
- **Owned by the server:** anything spawned with `NetworkServer.Spawn(obj)` (no owner). Its `SampleInput()` runs on the server, which is how you drive AI and bots.
- **On a host,** the host's own player is server-owned too (connection 0). It runs on the server directly, without prediction.

Because `SampleInput()` runs on whichever machine owns the object, give it an input source that makes sense there: the keyboard for a player, an AI brain for a bot. Don't assume `isLocalPlayer`, because ownership can change.

**Spawn predicted objects at runtime instead of placing them in the scene.** Mirror starts scene objects in no fixed order, so a scene-placed one can start before the adapter has created the prediction manager.

Despawn with `NetworkServer.Destroy(obj)` as usual. The binding then releases ownership, removes the entity from prediction on every machine, and re-parents its visuals.

### Changing the owner at runtime

Entering a vehicle, possessing a unit and picking up a ball all mean giving control of an existing object to someone else. Do it on the server:

```csharp
using Mirror;
using Sector0.Ursitoare;
using Sector0.UrsitoareMirror;

public static class PredictionOwnership
{
    // Give a client control of the object.
    public static void GiveTo(AbstractPredictedNetworkBehaviour obj, NetworkConnectionToClient conn)
    {
        ServerPredictionManager.Instance.SetEntityOwner(obj.serverPredictedEntity, conn.connectionId);

        // Optional: move Mirror authority along with it (see below).
        if (obj.netIdentity.connectionToClient != null)
            obj.netIdentity.RemoveClientAuthority();
        obj.netIdentity.AssignClientAuthority(conn);
    }

    // Give control back to the server (connection 0).
    public static void ReturnToServer(AbstractPredictedNetworkBehaviour obj)
    {
        ServerPredictionManager.Instance.SetEntityOwner(obj.serverPredictedEntity, 0);

        if (obj.netIdentity.connectionToClient != null)
            obj.netIdentity.RemoveClientAuthority();
    }
}
```

What happens during a handover:

- The server tells the old owner and the new owner over the reliable channel. Both reset the object's history, and the new owner starts sending input from its next tick.
- The server buffers `ServerPredictedEntity.BUFFER_FULL_THRESHOLD` ticks of the new owner's input before applying it, so the handover takes a few ticks.
- **Prediction ownership and Mirror authority are separate.** `SetEntityOwner` doesn't change `isOwned`, `connectionToClient` or `GetOwnerId()`. Prediction doesn't need Mirror authority, but if your own code relies on it, move it as in the example. Mirror won't move authority away from a player object, so only transfer prediction ownership for those.

**Handle disconnects.** When a client disconnects, Mirror destroys its player and every object it has Mirror authority over. Objects that you gave it only prediction ownership of stay assigned to the dead connection and stop receiving input. Return them to the server:

```csharp
using System.Collections.Generic;
using Mirror;
using Sector0.Ursitoare;
using Sector0.Ursitoare.Components;

public class MyNetworkManager : NetworkManager
{
    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        HashSet<ServerPredictedEntity> owned = ServerPredictionManager.Instance.GetEntitiesByOwner(conn.connectionId);
        if (owned != null)
        {
            foreach (ServerPredictedEntity entity in new List<ServerPredictedEntity>(owned))
                ServerPredictionManager.Instance.SetEntityOwner(entity, 0);
        }
        base.OnServerDisconnect(conn);
    }
}
```

### Checking who controls an object

```csharp
// On a client: is this object mine?
bool mine = ClientPredictionManager.Instance.IsControlledLocally(obj.netId);

// On the server or host:
bool serverControls = ServerPredictionManager.Instance.IsServerOwned(obj.serverPredictedEntity);
int ownerConnectionId = ServerPredictionManager.Instance.GetOwner(obj.serverPredictedEntity);
```

There is no event when ownership changes. Poll these values where you need them, for example in `Update` to switch cameras.

## 8. Tuning

### Where to apply settings

Most settings are static fields, and several are read only when an object spawns. Set them before predicted objects spawn. The adapter's `onReady` event fires when it creates the prediction manager, at server or client start:

```csharp
using Sector0.Ursitoare;
using Sector0.Ursitoare.Components;
using Sector0.Ursitoare.Interpolation;
using Sector0.Ursitoare.Resimulation.Detection;
using Sector0.UrsitoareMirror;
using UnityEngine;

public class PredictionConfig : MonoBehaviour
{
    [SerializeField] NetworkPredictionManagerAdapter adapter;

    void Awake() => adapter.onReady.AddEventListener(Apply);
    void OnDestroy() => adapter.onReady.RemoveEventListener(Apply);

    void Apply(bool _)
    {
        ServerPredictedEntity.BUFFER_FULL_THRESHOLD = 5;
        PredictionManager.SNAPSHOT_INSTANCE_RESIM_CHECKER = new SimpleConfigurableResimulationDecider(0.01f, 0.01f, 0.01f, 0.01f);
        MovingAverageInterpolator.FOLLOWER_SMOOTH_WINDOW = 4;
    }
}
```

### Core settings

The "Demo" column shows the values used in the reference demo, which are a tested starting point.

| What | Setting | Default | Demo | Effect |
|---|---|---|---|---|
| Tick rate | Adapter › Simulation Rate | 60 Hz | 120 Hz | Simulation and input rate: one input and one state message per object per tick. Higher is more responsive but costs more CPU and bandwidth. Interpolators read it when they're created, so set it in the Inspector and don't change it at runtime. |
| Send rate | Adapter › Network Send Rate | 30 | 60 | How often Mirror flushes its batched messages. Below the tick rate, messages arrive in bursts of tick rate ÷ send rate. |
| History | Buffer Size on each predicted behaviour | 50 | 50 | Ticks of input and state history. It must cover the round trip in ticks, plus the server input buffer, plus a margin; otherwise the client freezes until the server catches up. Keep it at or below the adapter's Physics History Buffer Size (120 by default). |
| Server input buffer | `ServerPredictedEntity.BUFFER_FULL_THRESHOLD` | 3 | 5 | Ticks of a client's input the server queues before applying it. More absorbs network jitter, so the owner gets fewer corrections, but everyone else sees the object later. |
| Server catch-up | `ServerPredictedEntity.CATCHUP`, `CATCHUP_SECTIONS` | on, 3 | on, 10 | When a client's input queue grows, the server applies several inputs per tick. A higher sections value starts catching up sooner. Read at spawn. |
| Correction threshold, own objects | `PredictionManager.SNAPSHOT_INSTANCE_RESIM_CHECKER` = `new SimpleConfigurableResimulationDecider(distance, angle°, velocity, angularVelocity)` | 0.0001, 0.0001, 0.001, 0.001 | 0.01 each | Error between prediction and server that triggers a rewind and replay. Lower is more exact but replays more often (CPU). Higher tolerates small errors, then fixes them in bigger steps. Read at spawn. |
| Correction threshold, followers | `PredictionManager.FOLLOWER_INSTANCE_RESIM_CHECKER` | same as above | – | The same for objects you don't control. Looser values stop other players from triggering replays. Read at spawn. |
| Predict followers | `PredictionManager.PREDICT_FOLLOWERS` | true | true | Simulate other objects forward between server updates. Keep it `true`: with `false`, other players snap to the latest server state, and objects without input aren't corrected at all. |
| Visual smoothing | `MovingAverageInterpolator.FOLLOWER_SMOOTH_WINDOW` | 4 | 4 | Number of ticks averaged when drawing each predicted object. Despite the name, it applies to every object. Bigger is smoother but adds visual delay. |
| Replay rate cap | `PredictionManager.Instance.minTicksBetweenResims` (with `protectFromOversimulation` and `oversimProtectWithTickInterval` on, the default) | 0 (no cap) | off | Allows at most one replay every N ticks. Caps CPU use, but corrections arrive later. This is an instance field, so set it in `onReady`. |

### How to tune

1. **Set the tick rate and send rate first.** Everything else is measured in ticks.
2. **Test under bad network conditions.** Wrap your transport in Mirror's `LatencySimulation` (latency, jitter, packet loss), run a server or host, and connect a separate client build.
3. **Watch the numbers.** `PredictionManager.Instance` has `totalResimulations`, `totalResimulationSteps` and `totalTickFreezes`. For per-tick warnings, subscribe in `onReady` to `PredictionManager.Instance.onTickStat` (tick and replay durations), `onPacketLoss` (client only) and `PredictedEntityVisuals.onLargeTransformJumpGlobal`.
4. **Match symptoms to fixes:**

| Symptom | Try |
|---|---|
| Replays every tick, even without latency | Something breaks [the rule](#4-the-rule-all-movement-goes-in-applyforces), the client and server simulate different things, or the thresholds are too tight. |
| Your own object rubber-bands under jitter | Raise `BUFFER_FULL_THRESHOLD`. |
| Freezes or hitches at high ping | Raise Buffer Size. |
| CPU spikes from replays | Loosen the thresholds, set `minTicksBetweenResims`, or predict fewer objects. |
| Other players jitter | Loosen the follower threshold, or raise `FOLLOWER_SMOOTH_WINDOW`. |
| Visuals feel late | Lower `FOLLOWER_SMOOTH_WINDOW`, or raise the tick rate. |

## 9. Debugging

- **Ghosts.** Set `PredictedEntityVisuals.SHOW_DBG = true` and give `PredictedEntityVisuals` a Server and/or Client Ghost Prefab (any small mesh without a collider). The server ghost shows the latest server state; the client ghost shows the raw physics body.
- **Messages.** `NetworkPredictionManagerAdapter.MSG_DEBUG = true` logs every prediction message, and `DEBUG = true` logs every tick.
- **Library logs.** These are off by default: `PredictionManager.LOG_EVENTS` (spawns, ownership, snaps), `LOG_ERRORS` and `DEBUG`.
- **Raw prediction.** `PredictionManager.DO_RESIM = false` turns corrections off, so you see the uncorrected prediction.

Logging is expensive at high tick rates, so turn it on only while you investigate.

## 10. Known limitations

- Settings are global (static fields), not per object.
- A client that controls nothing (a spectator) does not correct objects that have no input, such as props.
- See the [Ursitoare documentation](https://github.com/iFlex/Ursitoare) for how the library works internally and for its full scripting API.
