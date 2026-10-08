using System.Collections.Generic;
using Mirror;
using UnityEngine;
using Sector0.Events;
using Sector0.Ursitoare;
using Sector0.Ursitoare.Components;
using Sector0.Ursitoare.Data;
using Sector0.Ursitoare.Simulation;

namespace Sector0.UrsitoareMirror
{
    public class NetworkPredictionManagerAdapter : NetworkBehaviour
    {
        public static bool DEBUG = false;
        public static bool MSG_DEBUG = false;
        public static NetworkPredictionManagerAdapter instance;
        
        PredictionManager predictionManager;
        //TODO: remove the need for these 2 instances and use closures for handling messages from the server
        ClientPredictionManager _clientPredictionManager;
        ServerPredictionManager _serverPredictionManager;
        
        //TODO: offer a way to wire in PhysicsControllers
        
        public bool hasClientPredManager;
        public bool hasServerPredManager;
        
        public bool useUpdateLoop = false;
        public bool useGameTime;

        [SerializeField] private int InvalidConnectionId = -1;
        [SerializeField] private int ServerConnectionId = 0;
        
        void Awake()
        {
            instance = this;
        }
        
        public override void OnStartServer()
        {
            SetupServer();
        }
        
        public override void OnStartClient()
        {
            if (!isServer)
                SetupClient();
        }

        void SetupClient()
        {
            Debug.Log($"[PredictionMirrorBridge][clientStateSender] SETUP CLIENT SENDER CALLBACK");
            _clientPredictionManager = new ClientPredictionManager((tickId) =>
            {
                if (MSG_DEBUG)
                    Debug.Log(
                        $"[PredictionMirrorBridge][clientHeartbeadSender] SEND client_heartbeat tickId:{tickId}");
                ReportHeartbeat(tickId);
            },(tickId, entityId, data) =>
            {
                if (MSG_DEBUG)
                    Debug.Log($"[PredictionMirrorBridge][clientStateSender] SEND client_report: tickId:{tickId} entityId:{entityId} data:{data}");
                    
                ReportToServerUnreliable(tickId, entityId, data);
            });
            _clientPredictionManager.onTickStat.AddEventListener(OnTickStat);
            _clientPredictionManager.onPacketLoss.AddEventListener(OnPacketLoss);
            PredictedEntityVisuals.onLargeTransformJumpGlobal.AddEventListener(OnLargeTransformJump);
            predictionManager = _clientPredictionManager;
            hasClientPredManager = true;
            predictionManager.SetPhysicsController(new RewindablePhysicsController(120));
            onReady.Dispatch(true);
        }
        
        void SetupServer()
        {
            Debug.Log($"[PredictionMirrorBridge][clientStateSender] SETUP SERVER SEND CALLBACK");
                _serverPredictionManager = new ServerPredictionManager(InvalidConnectionId, ServerConnectionId, (connId, entityId, data) =>
                {
                    if (MSG_DEBUG)
                        Debug.Log($"[PredictionMirrorBridge][clientStateSender] SEND server_report: netId:{entityId} tickId:{data.tickId} data:{data}");

                    NetworkConnectionToClient netconn = GetNetConn(connId);
                    if (netconn != null)
                    {
                        TargetedReportFromServerUnreliable(netconn, entityId, data);
                    }
                    else if (connId != 0)
                    {
                        //TODO: report?
                    }
                }, (connId, data) =>
                {
                    if (MSG_DEBUG)
                        Debug.Log($"[PredictionMirrorBridge][serverWorldStateSender] SEND server_world_report: connId:{connId} data:{data}");
                    
                    NetworkConnectionToClient netconn = GetNetConn(connId);
                    if (netconn != null)
                    {
                        TargetedWorldReportFromServerUnreliable(netconn, data);
                    }
                    else if (connId != 0)
                    {
                        //TODO: report?
                    }
                }, (connId, entityId, owned) =>
                {
                    NetworkConnectionToClient netconn = GetNetConn(connId);
                    Debug.Log($"[NetworkPredictionManagerAdapter][reliableServerSetControlledLocally] connId:{connId} entityId:{entityId} owned:{owned} owned:{owned} netconn:{netconn}");
                    if (netconn != null)
                    {
                        UpdateLocalOwnership(netconn, entityId, owned);
                    }
                    else if (connId != 0)
                    {
                        //TODO: report?
                    }
                }, () => NetworkServer.connections.Keys);
                predictionManager = _serverPredictionManager;
                hasServerPredManager = true;
                predictionManager.SetPhysicsController(new RewindablePhysicsController(120));
                onReady.Dispatch(true);
        }

        private NetworkConnectionToClient GetNetConn(int connId)
        {
            if (connId == 0)
                return null;
            return NetworkServer.connections.GetValueOrDefault(connId, null);
        }

        private void FixedUpdate()
        {
            if (DEBUG)
                Debug.Log($"[NetworkPredictionManagerAdapter][Tick] t:{predictionManager.GetTickId()} time:{Time.realtimeSinceStartup} uul:{useUpdateLoop} mdt:{Time.maximumDeltaTime}");
            if (!useUpdateLoop)
            {
                predictionManager.Tick();
            }
        }

        [Command(requiresAuthority = false, channel = Channels.Unreliable)]
        void ReportHeartbeat(uint tickId, NetworkConnectionToClient sender = null)
        {
            if (MSG_DEBUG)
                Debug.Log($"[PredictionMirrorBridge][ReportHeartbeat] RECV client_heartbeat: tickId:{tickId} sender:{sender}");
            _serverPredictionManager.OnHeartbeatReceived(sender.connectionId, tickId);
        }
        
        [Command(requiresAuthority = false, channel = Channels.Unreliable)]
        void ReportToServerUnreliable(uint tickId, uint entityId, PredictionInputRecord data, NetworkConnectionToClient sender = null)
        {
            if (MSG_DEBUG)
                Debug.Log($"[PredictionMirrorBridge][ReportToServerUnreliable] RECV client_report: tickId:{tickId} entityId:{entityId} sender:{sender} data:{data}");
            _serverPredictionManager.OnClientStateReceived(sender.connectionId, tickId, entityId, data);
        }
        
        [TargetRpc(channel = Channels.Unreliable)]
        void TargetedReportFromServerUnreliable(NetworkConnectionToClient receiver, uint entityNetId, PhysicsStateRecord data)
        {
            if (MSG_DEBUG)
                Debug.Log($"[PredictionMirrorBridge][TargetedReportFromServerUnreliable] RECV serrver_report: netId:{entityNetId} tickId:{data.tickId} data:{data}");
            _clientPredictionManager.OnServerStateReceived(entityNetId, data);
        }
        
        [TargetRpc(channel = Channels.Unreliable)]
        void TargetedWorldReportFromServerUnreliable(NetworkConnectionToClient receiver, WorldStateRecord data)
        {
            if (MSG_DEBUG)
                Debug.Log($"[PredictionMirrorBridge][TargetedWorldReportFromServerUnreliable] RECV server_report: data:{data}");
            _clientPredictionManager.OnServerWorldStateReceived(data);
        }
        
        [TargetRpc(channel = Channels.Reliable)]
        void UpdateLocalOwnership(NetworkConnectionToClient receiver, uint entityId, bool owned)
        {
            if (MSG_DEBUG)
                Debug.Log($"[PredictionMirrorBridge][UpdateLocalOwnership] Received server_ownership_report: entity:{entityId} owned:{owned}");
            _clientPredictionManager.OnEntityOwnershipChanged(entityId, owned);
        }
        

        void OnPacketLoss(int lostCount)
        {
            Debug.Log($"[NetworkPredictionManagerAdapter][WARNING][PACKET_LOSS] tickId:{PredictionManager.Instance.GetTickId()} lost:{lostCount}");        
        }
        
        void OnTickStat(PredictionManager.TickStat tickStat)
        {
            if (tickStat.duration >= Time.fixedDeltaTime * 0.75f)
            {
                Debug.Log($"[NetworkPredictionManagerAdapter][WARNING][HEAVY_TICK] tickId:{tickStat.tickId} tickTime:{tickStat.duration} FixedDTime:{Time.fixedDeltaTime} resimDuration:{tickStat.resimDuration} resimmedTicks:{tickStat.resimTicks}");        
            }
        }

        void OnLargeTransformJump(PredictedEntityVisuals.GlobalTransformJump transformJump)
        {
            Debug.Log($"[NetworkPredictionManagerAdapter][WARNING][LARGE_VISUAL_TRANSFORM_JUMP] tickId:{predictionManager.GetTickId()} pos:{transformJump.jump.positionDiff}|({transformJump.jump.positionDiff.magnitude}) rot:{transformJump.jump.rotationDiff.eulerAngles}");
        }

        private float timeSincePredTick = 0;
        private float lastWallClockUpdate = 0;
        void Update()
        {
            if (DEBUG) 
                Debug.Log($"[Prediction][Update] t:{Time.realtimeSinceStartup} uul:{useUpdateLoop} mdt:{Time.maximumDeltaTime}");

            if (useUpdateLoop)
            {
                float deltaWallClock = Time.realtimeSinceStartup - lastWallClockUpdate;
                if (useGameTime)
                {
                    timeSincePredTick += Time.deltaTime;
                }
                else
                {
                    timeSincePredTick += deltaWallClock;
                }
                
                if (timeSincePredTick >= Time.fixedDeltaTime)
                {
                    timeSincePredTick -= Time.fixedDeltaTime;
                    predictionManager.Tick();
                }
                
                lastWallClockUpdate = Time.realtimeSinceStartup;
            }
        }

        void OnDestroy()
        {
            instance = null;
            if (predictionManager != null)
            {
                predictionManager.onTickStat.RemoveEventListener(OnTickStat);
                predictionManager.onPacketLoss.RemoveEventListener(OnPacketLoss);
            }
            PredictedEntityVisuals.onLargeTransformJumpGlobal.RemoveEventListener(OnLargeTransformJump);
        }

        public SafeEventDispatcher<bool> onReady = new();
    }
}