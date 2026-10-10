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
        [SerializeField] private bool autoSetOwnership = true;
        public int BufferSize => bufferSize;
        
        public PredictedEntityVisuals visuals;
        public ClientPredictedEntity clientPredictedEntity { get; private set; }
        public ServerPredictedEntity serverPredictedEntity { get; private set; }

        //NOTE: visuals get detached from this object on spawn, remember where they lived so despawn can put them back.
        private Transform _visualsParent;
        private Vector3 _visualsLocalPosition;
        private Quaternion _visualsLocalRotation;
        private Vector3 _visualsLocalScale;

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
            if (autoSetOwnership)
            {
                int connId = (connectionToClient == null) ? 0 : connectionToClient.connectionId;
                ServerPredictionManager.Instance.SetEntityOwner(serverPredictedEntity, connId);   
            }
        }
        
        public override void OnStartClient()
        {
            if (!isServer)
            {
                ConfigureAsClient();
            }
        }
        
        public override void OnStopServer()
        {
            if (serverPredictedEntity == null)
                return;

            //NOTE: deregistering also drops the owner. The owning client isn't told, but its own despawn stops it controlling the entity.
            ((PredictedEntity)this).Deregister();
            serverPredictedEntity = null;
            ReattachVisuals();
        }

        public override void OnStopClient()
        {
            //NOTE: on a host the server side owns the entity, OnStopServer cleans it up.
            if (clientPredictedEntity == null)
                return;

            ((PredictedEntity)this).Deregister();
            clientPredictedEntity = null;
            ReattachVisuals();
        }

        void ConfigureAsServer()
        {
            serverPredictedEntity = new ServerPredictedEntity(netId, bufferSize, _rigidbody, visuals.gameObject, WrapperHelpers.GetControllableComponents(predictionComponents, this), WrapperHelpers.GetComponents(predictionComponents, this));
            ((PredictedEntity)this).Register();
            RememberVisualsParent();
            visuals.SetServerPredictedEntity(transform);
        }

        void ConfigureAsClient()
        {
            clientPredictedEntity = new ClientPredictedEntity(netId, false, bufferSize, _rigidbody, visuals.gameObject, WrapperHelpers.GetControllableComponents(predictionComponents, this), WrapperHelpers.GetComponents(predictionComponents, this));
            RememberVisualsParent();
            visuals.SetClientPredictedEntity(clientPredictedEntity, PredictionManager.INTERPOLATION_PROVIDER());
            ((PredictedEntity)this).Register();
        }

        void RememberVisualsParent()
        {
            Transform visualsTransform = visuals.visualsEntity.transform;
            _visualsParent = visualsTransform.parent;
            _visualsLocalPosition = visualsTransform.localPosition;
            _visualsLocalRotation = visualsTransform.localRotation;
            _visualsLocalScale = visualsTransform.localScale;
        }

        //Detached visuals would outlive a destroyed object, or stay visible after a scene object is disabled on unspawn.
        void ReattachVisuals()
        {
            if (!visuals || !visuals.visualsEntity || !_visualsParent)
                return;

            Transform visualsTransform = visuals.visualsEntity.transform;
            visualsTransform.SetParent(_visualsParent, false);
            visualsTransform.localPosition = _visualsLocalPosition;
            visualsTransform.localRotation = _visualsLocalRotation;
            visualsTransform.localScale = _visualsLocalScale;
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