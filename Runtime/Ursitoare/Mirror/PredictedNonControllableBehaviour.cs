using Prediction.Data;

namespace Sector0.UrsitoareMirror
{
    public class PredictedNonControllableBehaviour : AbstractPredictedNetworkBehaviour
    {
        public override void ApplyForces()
        {
        }

        public override bool HasState()
        {
            return false;
        }

        public override void SampleComponentState(PhysicsStateRecord physicsStateRecord) {}

        public override void LoadComponentState(PhysicsStateRecord physicsStateRecord) {}

        public override int GetStateFloatCount()
        {
            return 0;
        }

        public override int GetStateBoolCount()
        {
            return 0;
        }

        public override int GetFloatInputCount()
        {
            return 0;

        }

        public override int GetBinaryInputCount()
        {
            return 0;
        }

        public override void SampleInput(PredictionInputRecord input)
        {
        }

        public override bool ValidateInput(float deltaTime, PredictionInputRecord input)
        {
            return true;
        }

        public override void LoadInput(PredictionInputRecord input)
        {
        }

        public override void ClearInput()
        {
        }
    }
}