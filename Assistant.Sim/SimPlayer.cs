using System;
using Assistant.Core.Data;

namespace Assistant.Sim
{
    public class SimPlayer
    {
        public PlayerState State { get; }

        public SimPlayer(Vec3 initialPosition, float speed = 5.0f)
        {
            State = new PlayerState
            {
                Position = initialPosition,
                Speed = speed
            };
        }

        public void Teleport(Vec3 position)
        {
            State.Position = position;
        }

        public void MoveTowards(Vec3 target, float deltaTime)
        {
            float dist = Vec3.Distance(State.Position, target);
            float step = State.Speed * deltaTime;
            if (dist <= step || dist < 0.001f)
            {
                State.Position = target;
            }
            else
            {
                float ratio = step / dist;
                float nx = State.Position.X + (target.X - State.Position.X) * ratio;
                float ny = State.Position.Y + (target.Y - State.Position.Y) * ratio;
                float nz = State.Position.Z + (target.Z - State.Position.Z) * ratio;
                State.Position = new Vec3(nx, ny, nz);
            }
        }
    }
}
