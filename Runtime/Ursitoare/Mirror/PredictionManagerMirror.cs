using System.Collections.Generic;
using Mirror;
using Prediction.data;
using UnityEngine;

namespace Prediction.Wrappers
{
    public class PredictionManagerMirror : NetworkBehaviour
    {
        public static PredictionManagerMirror instance;
        public static bool MSG_DEBUG = false;
        PredictionManager predictionManager = new PredictionManager();
        
        public void Start()
        {
            Debug.Log($"[NetworkPredictionManagerAdapter] AppPath:{Application.dataPath}");
            Debug.Log($"[NetworkPredictionManagerAdapter] PdPath:{Application.persistentDataPath}");
            
            if (instance != null)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            
            if (isClient)
            {
                Debug.Log($"[PredictionMirrorBridge][clientStateSender] SETUP CLIENT SENDER CALLBACK");
                predictionManager.clientStateSender = (tickId, data) =>
                {
                    if (MSG_DEBUG)
                        Debug.Log($"[PredictionMirrorBridge][clientStateSender] SEND client_report: tickId:{tickId} data:{data}");
                    
                    ReportToServerUnreliable(tickId, data);
                };
                predictionManager.clientHeartbeadSender = (tickId) =>
                {
                    if (MSG_DEBUG)
                        Debug.Log(
                            $"[PredictionMirrorBridge][clientHeartbeadSender] SEND client_heartbeat tickId:{tickId}");
                    ReportHeartbeat(tickId);
                };
            }
            
            if (isServer)
            {
                Debug.Log($"[PredictionMirrorBridge][clientStateSender] SETUP SERVER SEND CALLBACK");
                predictionManager.connectionsIterator = () => NetworkServer.connections.Keys;
                predictionManager.serverStateSender = (connId, entityId, data) =>
                {
                    if (MSG_DEBUG)
                        Debug.Log($"[PredictionMirrorBridge][clientStateSender] SEND server_report: netId:{entityId} tickId:{data.tickId} data:{data}");
                    
                    NetworkConnectionToClient netconn = NetworkServer.connections.GetValueOrDefault(connId, null);
                    if (netconn != null)
                    {
                        TargetedReportFromServerUnreliable(netconn, entityId, data);
                    }
                    else if (connId != 0)
                    {
                        //TODO: report?
                    }
                };
                
                predictionManager.serverWorldStateSender = (connId, data) =>
                {
                    if (MSG_DEBUG)
                        Debug.Log($"[PredictionMirrorBridge][serverWorldStateSender] SEND server_world_report: connId:{connId} data:{data}");
                    
                    NetworkConnectionToClient netconn = NetworkServer.connections.GetValueOrDefault(connId, null);
                    if (netconn != null)
                    {
                        TargetedWorldReportFromServerUnreliable(netconn, data);
                    }
                    else if (connId != 0)
                    {
                        //TODO: report?
                    }
                };
                
                predictionManager.serverSetControlledLocally = (connId, entityId, owned) =>
                {
                    NetworkConnectionToClient netconn = NetworkServer.connections.GetValueOrDefault(connId, null);
                    if (netconn != null)
                    {
                        UpdateLocalOwnership(netconn, entityId, owned);
                    }
                    else if (connId != 0)
                    {
                        //TODO: report?
                    }
                };
            }
            SetupPrediction();
        }

        void SetupPrediction()
        {
            Debug.Log($"[NetworkPredictionManagerAdapter][SetupPrediction] isServer:{isServer} isClient:{isClient}");
            predictionManager.Setup(isServer, isClient);
        }

        private void FixedUpdate()
        {
            predictionManager.Tick();
        }

        [Command(requiresAuthority = false, channel = Channels.Unreliable)]
        void ReportHeartbeat(uint tickId, NetworkConnectionToClient sender = null)
        {
            if (MSG_DEBUG)
                Debug.Log($"[PredictionMirrorBridge][ReportHeartbeat] RECV client_heartbeat: tickId:{tickId} sender:{sender}");
            predictionManager.OnHeartbeatReceived(sender.connectionId, tickId);
        }
        
        [Command(requiresAuthority = false, channel = Channels.Unreliable)]
        void ReportToServerUnreliable(uint tickId, PredictionInputRecord data, NetworkConnectionToClient sender = null)
        {
            if (MSG_DEBUG)
                Debug.Log($"[PredictionMirrorBridge][ReportToServerUnreliable] RECV client_report: tickId:{tickId} sender:{sender} data:{data}");
            predictionManager.OnClientStateReceived(sender.connectionId, tickId, data);
        }
        
        [TargetRpc(channel = Channels.Unreliable)]
        void TargetedReportFromServerUnreliable(NetworkConnectionToClient receiver, uint entityNetId, PhysicsStateRecord data)
        {
            if (MSG_DEBUG)
                Debug.Log($"[PredictionMirrorBridge][TargetedReportFromServerUnreliable] RECV serrver_report: netId:{entityNetId} tickId:{data.tickId} data:{data}");
            predictionManager.OnServerStateReceived(entityNetId, data);
        }
        
        [TargetRpc(channel = Channels.Unreliable)]
        void TargetedWorldReportFromServerUnreliable(NetworkConnectionToClient receiver, WorldStateRecord data)
        {
            if (MSG_DEBUG)
                Debug.Log($"[PredictionMirrorBridge][TargetedWorldReportFromServerUnreliable] RECV server_report: data:{data}");
            predictionManager.OnServerWorldStateReceived(data);
        }
        
        [TargetRpc(channel = Channels.Reliable)]
        void UpdateLocalOwnership(NetworkConnectionToClient receiver, uint entityId, bool owned)
        {
            if (MSG_DEBUG)
                Debug.Log($"[PredictionMirrorBridge][UpdateLocalOwnership] Received server_ownership_report: entity:{entityId} owned:{owned}");
            predictionManager.OnEntityOwnershipChanged(entityId, owned);
        }

        [Server]
        void ServerReset()
        {
            Debug.Log("[NetworkPredictionManagerAdapter][ServerReset]");
            predictionManager.Clear();
            RpcReset();
        }
        
        [ClientRpc]
        void RpcReset()
        {
            Debug.Log("[NetworkPredictionManagerAdapter][RpcReset]");
            predictionManager.Clear();
        }
    }
}