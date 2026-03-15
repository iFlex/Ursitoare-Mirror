using Mirror;
using UnityEngine;

namespace Prediction.Wrappers
{
    public class PredictedNetworkBehaviour : NetworkBehaviour, PredictedEntity
    {
        //FUDO: can we make components serializable?
        [SerializeField] private MonoBehaviour[] components;
        [SerializeField] private int bufferSize = 50;
        [SerializeField] private Rigidbody _rigidbody;
        //TODO: private set but serializable...
        public PredictedEntityVisuals visuals;// { get; private set; }
        public ClientPredictedEntity clientPredictedEntity { get; private set; }
        public ServerPredictedEntity serverPredictedEntity { get; private set; }
        public bool isReady { get; private set; }
        
        [SerializeField] private bool dbgIsLocallyControlled;
        [SerializeField] private int dbgGOID;
        [SerializeField] private uint dbgNetID;

        void Awake()
        {
            if (components == null || components.Length == 0)
            {
                //
            }
        }
        private void SetReady(bool ready)
        {
            if (!isReady && ready)
            {
                ((PredictedEntity)this).Register();
            }
            isReady = ready;
        }
        
        void OnEnable()
        {
            if (isReady)
            {
                ((PredictedEntity)this).Register();
            }
        }

        void OnDisable()
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

        public override void OnStartAuthority()
        {
            //TODO: wat?
        }

        void ConfigureAsServer()
        {
            Debug.Log($"[PredictedNetworkBehaviour][ConfigureAsServer] this:{this} netId:{netId}");
            serverPredictedEntity = new ServerPredictedEntity(netId, bufferSize, _rigidbody, visuals.gameObject, WrapperHelpers.GetControllableComponents(components), WrapperHelpers.GetComponents(components));
            ((PredictedEntity)this).Register();
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
            Debug.Log($"[PredictedNetworkBehaviour][ResetClient]({netId})");
            visuals.Reset();
            clientPredictedEntity?.Reset();
        }
        
        public void Reset()
        {
            Debug.Log($"[PredictedNetworkBehaviour][Reset]({netId})");
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

        void Update()
        {
            dbgGOID = gameObject.GetInstanceID();
            dbgNetID = netId;
            dbgIsLocallyControlled = IsControlledLocally();
        }
    }
}