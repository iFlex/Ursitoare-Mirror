using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using Sector0.Events;
using Sector0.Ursitoare;
using Sector0.Ursitoare.Data;
using Sector0.Ursitoare.Simulation;

namespace Sector0.UrsitoareMirror
{
    public class NetworkPredictionManagerAdapter : NetworkBehaviour
    {
        public static bool DEBUG = false;
        public static bool MSG_DEBUG = false;
        public static NetworkPredictionManagerAdapter Instance;
        
        PredictionManager predictionManager;
        ClientPredictionManager _clientPredictionManager;
        ServerPredictionManager _serverPredictionManager;
        
        //TODO: offer a way to wire in PhysicsControllers
        
        private int InvalidConnectionId = -1;
        private int ServerConnectionId = 0;
        [SerializeField] [Min(1)] private float simulationRate = 60;
        [SerializeField] [Min(1)] private int networkSendRate = 30;
        [SerializeField] [Min(1)] private int PhysicsHistoryBufferSize = 120;
        
        void Awake()
        {
            if (Instance)
            {
                throw new Exception("Multiple NetworkPredictionManagerAdapters detected");
            }
            Instance = this;
        }
        
        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
        
        //NOTE: Mirror starts a duplicate adapter even though its Awake threw, so only the registered Instance sets up.
        public override void OnStartServer()
        {
            if (Instance == this)
                SetupServer();
            SetupApplication();
        }

        public override void OnStartClient()
        {
            if (Instance == this && !isServer)
                SetupClient();
            SetupApplication();
        }

        protected virtual void SetupApplication()
        {
            Time.fixedDeltaTime = 1f / simulationRate;
            NetworkManager.singleton.sendRate = networkSendRate;
            if (Application.targetFrameRate > 0 && Application.targetFrameRate < networkSendRate)
            {
                //NOTE: Mirror runs in lateUpdate on the Update loop, hence it is affected by the target frame rate.
                Application.targetFrameRate = networkSendRate;
            }
        }

        void SetupClient()
        {
            Debug.Log($"[NetworkPredictionManagerAdapter][SetupClient] SETUP CLIENT SENDER CALLBACK");
            _clientPredictionManager = new ClientPredictionManager((tickId) =>
            {
                if (MSG_DEBUG)
                    Debug.Log(
                        $"[NetworkPredictionManagerAdapter][SetupClient] SEND client_heartbeat tickId:{tickId}");
                ReportHeartbeat(tickId);
            },(tickId, entityId, data) =>
            {
                if (MSG_DEBUG)
                    Debug.Log($"[NetworkPredictionManagerAdapter][SetupClient] SEND client_report: tickId:{tickId} entityId:{entityId} data:{data}");
                    
                ReportToServerUnreliable(tickId, entityId, data);
            });
            
            predictionManager = _clientPredictionManager;
            predictionManager.SetPhysicsController(new RewindablePhysicsController(PhysicsHistoryBufferSize));
            
            onReady.Dispatch(true);
        }
        
        void SetupServer()
        {
            Debug.Log($"[NetworkPredictionManagerAdapter][SetupServer] SETUP SERVER SEND CALLBACK");
            _serverPredictionManager = new ServerPredictionManager(InvalidConnectionId, ServerConnectionId, (connId, entityId, data) =>
            {
                if (MSG_DEBUG)
                    Debug.Log($"[NetworkPredictionManagerAdapter][SetupServer] SEND server_report: netId:{entityId} tickId:{data.tickId} data:{data}");

                NetworkConnectionToClient netconn = GetNetConn(connId);
                if (netconn != null)
                {
                    TargetedReportFromServerUnreliable(netconn, entityId, data);
                }
            }, (connId, data) =>
            {
                if (MSG_DEBUG)
                    Debug.Log($"[NetworkPredictionManagerAdapter][SetupServer] SEND server_world_report: connId:{connId} data:{data}");
                
                NetworkConnectionToClient netconn = GetNetConn(connId);
                if (netconn != null)
                {
                    TargetedWorldReportFromServerUnreliable(netconn, data);
                }
            }, (connId, entityId, owned) =>
            {
                NetworkConnectionToClient netconn = GetNetConn(connId);
                if (netconn != null)
                {
                    UpdateLocalOwnership(netconn, entityId, owned);
                }
            }, () => NetworkServer.connections.Keys);
            
            predictionManager = _serverPredictionManager;
            predictionManager.SetPhysicsController(new RewindablePhysicsController(PhysicsHistoryBufferSize));
            
            onReady.Dispatch(true);
        }

        private NetworkConnectionToClient GetNetConn(int connId)
        {
            if (connId == ServerConnectionId)
                return null;
            return NetworkServer.connections.GetValueOrDefault(connId, null);
        }

        private void FixedUpdate()
        {
            //NOTE: during a client's initial spawn Mirror activates this object before OnStartClient creates the manager.
            if (predictionManager == null)
                return;
            if (DEBUG)
                Debug.Log($"[NetworkPredictionManagerAdapter][Tick] t:{predictionManager.GetTickId()} time:{Time.realtimeSinceStartup} mdt:{Time.maximumDeltaTime}");
            predictionManager.Tick();
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

        public SafeEventDispatcher<bool> onReady = new();
    }
}