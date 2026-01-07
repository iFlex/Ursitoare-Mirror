using System.Collections.Generic;
using Mirror;
using Prediction.data;
using UnityEngine;

namespace Prediction.Wrappers
{
    public class NetworkPredictedManager : NetworkBehaviour
    {
        public static bool MSG_DEBUG = false;
        PredictionManager predictionManager = new PredictionManager();
        
        private void Start()
        {
            PredictionManager.ROUND_TRIP_GETTER = () => NetworkTime.rtt;
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
            }
            predictionManager.Setup(isServer, isClient);
        }

        [Command(requiresAuthority = false, channel = Channels.Unreliable)]
        void ReportHeartbeat(uint tickId, NetworkConnectionToClient sender = null)
        {
            if (MSG_DEBUG)
                Debug.Log($"[PredictionMirrorBridge][ReportHeartbeat] Received client_heartbeat: tickId:{tickId} sender:{sender}");
            predictionManager.OnHeartbeatReceived(sender.connectionId, tickId);
        }
        
        [Command(requiresAuthority = false, channel = Channels.Unreliable)]
        void ReportToServerUnreliable(uint tickId, PredictionInputRecord data, NetworkConnectionToClient sender = null)
        {
            if (MSG_DEBUG)
                Debug.Log($"[PredictionMirrorBridge][ReportToServerUnreliable] Received client_report: tickId:{tickId} sender:{sender} data:{data}");
            predictionManager.OnClientStateReceived(sender.connectionId, tickId, data);
        }
        
        [TargetRpc(channel = Channels.Unreliable)]
        void TargetedReportFromServerUnreliable(NetworkConnectionToClient receiver, uint entityNetId, PhysicsStateRecord data)
        {
            if (MSG_DEBUG)
                Debug.Log($"[PredictionMirrorBridge][TargetedReportFromServerUnreliable] Received serrver_report: netId:{entityNetId} tickId:{data.tickId} data:{data}");
            predictionManager.OnServerStateReceived(entityNetId, data);
        }
        
        [TargetRpc(channel = Channels.Unreliable)]
        void TargetedWorldReportFromServerUnreliable(NetworkConnectionToClient receiver, WorldStateRecord data)
        {
            if (MSG_DEBUG)
                Debug.Log($"[PredictionMirrorBridge][TargetedWorldReportFromServerUnreliable] Received server_report: data:{data}");
            predictionManager.OnServerWorldStateReceived(data);
        }
    }
}