namespace Assistant.Core.Data
{
    public class PlayerState
    {
        public Vec3 Position { get; set; } = Vec3.Zero;
        public float Speed { get; set; } = 5f;

        public PlayerState Clone()
        {
            return new PlayerState
            {
                Position = Position,
                Speed = Speed
            };
        }

        public override string ToString() => $"Player[Pos={Position}, Spd={Speed}]";
    }
}
