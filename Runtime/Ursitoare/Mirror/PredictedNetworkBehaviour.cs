using Mirror;
using Prediction.Components.Controllers;
using UnityEngine;

namespace Prediction.Components
{
    public class PredictedNetworkBehaviour : NetworkBehaviour, PredictedEntity
    {
        //FUDO: can we make components serializable?
        [SerializeField] private MonoBehaviour[] components;
        [SerializeField] private int bufferSize = 50;
        [SerializeField] private Rigidbody _rigidbody;
        
        public PredictedEntityVisuals visuals;
        public ClientPredictedEntity clientPredictedEntity { get; private set; }
        public ServerPredictedEntity serverPredictedEntity { get; private set; }
        public bool isReady { get; private set; }

        protected virtual void Awake()
        {
            if (components == null || components.Length == 0)
            {
                components = AutoDetectComponents();
            }
            if (_rigidbody == null)
            {
                _rigidbody = GetComponentInChildren<Rigidbody>();
            }
        }

        MonoBehaviour[] AutoDetectComponents()
        {
            //Auto detect components
            PredictableComponent[] predictables = gameObject.GetComponentsInChildren<PredictableComponent>();
            PredictableControllableComponent[] predictableControllables = gameObject.GetComponentsInChildren<PredictableControllableComponent>();
            
            MonoBehaviour[] componentsCopy = new MonoBehaviour[predictables.Length + predictableControllables.Length];
            int j = 0;
            for (int i = 0; i < predictables.Length; i++)
            {
                componentsCopy[j++] = (MonoBehaviour) predictables[i];
            }
            for (int i = 0; i < predictableControllables.Length; i++)
            {
                componentsCopy[j++] = (MonoBehaviour) predictableControllables[i];
            }
            return componentsCopy;
        }
        
        private void SetReady(bool ready)
        {
            if (!isReady && ready)
            {
                ((PredictedEntity)this).Register();
            }
            isReady = ready;
        }
        
        protected virtual void OnEnable()
        {
            if (isReady)
            {
                ((PredictedEntity)this).Register();
            }
        }

        protected virtual void OnDisable()
        {
            ((PredictedEntity)this).Deregister();
        }
        
        public override void OnStartServer()
        {
            ConfigureAsServer();
            if (isServerOnly)
            {
                SetReady(true);
            }
        }
    
        public override void OnStartClient()
        {
            if (isServer)
            {
                ConfigureAsServerClient();
            }
            else
            {
                ConfigureAsClient();
            }
            SetReady(true);
        }

        void ConfigureAsServer()
        {
            Debug.Log($"[PredictedNetworkBehaviour][ConfigureAsServer] this:{this} netId:{netId}");
            serverPredictedEntity = new ServerPredictedEntity(netId, bufferSize, _rigidbody, visuals.gameObject, WrapperHelpers.GetControllableComponents(components), WrapperHelpers.GetComponents(components));
            ((PredictedEntity)this).Register();
            visuals.SetServerPredictedEntity(transform);
        }

        void ConfigureAsClient()
        {
            Debug.Log($"[PredictedNetworkBehaviour][ConfigureAsClient] this:{this} netId:{netId}");
            clientPredictedEntity = new ClientPredictedEntity(netId, false, bufferSize, _rigidbody, visuals.gameObject, WrapperHelpers.GetControllableComponents(components), WrapperHelpers.GetComponents(components));
            visuals.SetClientPredictedEntity(clientPredictedEntity, PredictionManager.INTERPOLATION_PROVIDER());
            ((PredictedEntity)this).Register();
        }

        void ConfigureAsServerClient()
        {
            Debug.Log($"[PredictedNetworkBehaviour][ConfigureAsServerClient] this:{this} netId:{netId}");
            clientPredictedEntity = new ClientPredictedEntity(netId, true, bufferSize, _rigidbody, visuals.gameObject, WrapperHelpers.GetControllableComponents(components), WrapperHelpers.GetComponents(components));
            visuals.SetServerPredictedEntity(transform);
            ((PredictedEntity)this).Register();
        }
        
        public bool IsControlledLocally()
        {
            if (clientPredictedEntity == null)
                return true;
            return clientPredictedEntity.isControlledLocally;
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

        public PredictedEntityVisuals GetVisualsControlled()
        {
            return visuals;
        }
        
        public void ResetClient()
        {
            visuals.Reset();
            clientPredictedEntity?.Reset();
        }
        
        public void Reset()
        {
            ResetClient();
            serverPredictedEntity?.Reset();
        }
        
        public ClientPredictedEntity GetClientEntity()
        {
            return clientPredictedEntity;
        }

        public ServerPredictedEntity GetServerEntity()
        {
            return serverPredictedEntity;
        }
        
        public virtual uint GetId()
        {
            return netId;
        }

        public virtual int GetOwnerId()
        {
            return (netIdentity.connectionToClient == null) ? 0 : netIdentity.connectionToClient.connectionId;
        }
    }
}