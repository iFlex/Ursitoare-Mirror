using Mirror;
using Sector0.Ursitoare;
using Sector0.Ursitoare.Components;
using Sector0.Ursitoare.Data;
using Sector0.Ursitoare.Wrappers;
using UnityEngine;

namespace Sector0.UrsitoareMirror
{
    public abstract class AbstractPredictedNetworkBehaviour : NetworkBehaviour, PredictedEntity, PredictableComponent, PredictableControllableComponent
    {
        [SerializeField] private MonoBehaviour[] predictionComponents;
        [SerializeField] private Rigidbody _rigidbody;
        [SerializeField] protected int bufferSize = 50;
        public int BufferSize => bufferSize;
        
        public PredictedEntityVisuals visuals;
        public ClientPredictedEntity clientPredictedEntity { get; private set; }
        public ServerPredictedEntity serverPredictedEntity { get; private set; }
        

        protected void Awake()
        {
            if (!_rigidbody)
            {
                _rigidbody = GetComponent<Rigidbody>();
            }
        }
        
        public override void OnStartServer()
        {
            ConfigureAsServer();
            int connId = (connectionToClient == null) ? 0 : connectionToClient.connectionId;
            ServerPredictionManager.Instance.SetEntityOwner(serverPredictedEntity, connId);
        }
        
        public override void OnStartClient()
        {
            if (!isServer)
            {
                ConfigureAsClient();
            }
        }
        
        void ConfigureAsServer()
        {
            serverPredictedEntity = new ServerPredictedEntity(netId, bufferSize, _rigidbody, visuals.gameObject, WrapperHelpers.GetControllableComponents(predictionComponents, this), WrapperHelpers.GetComponents(predictionComponents, this));
            ((PredictedEntity)this).Register();
            visuals.SetServerPredictedEntity(transform);
        }

        void ConfigureAsClient()
        {
            clientPredictedEntity = new ClientPredictedEntity(netId, false, bufferSize, _rigidbody, visuals.gameObject, WrapperHelpers.GetControllableComponents(predictionComponents, this), WrapperHelpers.GetComponents(predictionComponents, this));
            visuals.SetClientPredictedEntity(clientPredictedEntity, PredictionManager.INTERPOLATION_PROVIDER());
            ((PredictedEntity)this).Register();
        }
        
        public uint GetId()
        {
            return netId;
        }

        public int GetOwnerId()
        {
            return (netIdentity.connectionToClient == null) ? 0 : netIdentity.connectionToClient.connectionId;
        }

        public ClientPredictedEntity GetClientEntity()
        {
            return clientPredictedEntity;
        }

        public ServerPredictedEntity GetServerEntity()
        {
            return serverPredictedEntity;
        }

        public PredictedEntityVisuals GetVisualsControlled()
        {
            return visuals;
        }

        public bool IsServer()
        {
            return isServer;
        }

        public bool IsClient()
        {
            return isClient;
        }

        public Rigidbody GetRigidbody()
        {
            return _rigidbody;
        }


        public abstract void ApplyForces();
        public abstract bool HasState();
        public abstract void SampleComponentState(PhysicsStateRecord physicsStateRecord);
        public abstract void LoadComponentState(PhysicsStateRecord physicsStateRecord);
        public abstract int GetStateFloatCount();
        public abstract int GetStateBoolCount();
        public abstract int GetFloatInputCount();
        public abstract int GetBinaryInputCount();
        public abstract void SampleInput(PredictionInputRecord input);
        public abstract bool ValidateInput(float deltaTime, PredictionInputRecord input);
        public abstract void LoadInput(PredictionInputRecord input);
        public abstract void ClearInput();
    }
}