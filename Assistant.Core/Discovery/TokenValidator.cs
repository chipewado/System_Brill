using System;
using Assistant.Core.Abstractions;
using Assistant.Core.Data;

namespace Assistant.Core.Discovery
{
    public enum ValidationFailureReason
    {
        None,
        NullToken,
        InvalidId,
        InvalidPosition,
        OutOfBounds,
        AlreadyCollected,
        NegativeValue,
        StaleToken
    }

    public class TokenValidationResult
    {
        public bool IsValid => Reason == ValidationFailureReason.None;
        public ValidationFailureReason Reason { get; }
        public string Message { get; }

        public TokenValidationResult(ValidationFailureReason reason, string message)
        {
            Reason = reason;
            Message = message;
        }

        public static TokenValidationResult Success() =>
            new TokenValidationResult(ValidationFailureReason.None, "Valid");

        public static TokenValidationResult Failure(ValidationFailureReason reason, string message) =>
            new TokenValidationResult(reason, message);
    }

    public class TokenValidator
    {
        private readonly IClock _clock;
        public float DefaultTtlSeconds { get; set; } = 30f;

        public TokenValidator(IClock? clock = null, float defaultTtlSeconds = 30f)
        {
            _clock = clock ?? SystemClock.Instance;
            DefaultTtlSeconds = defaultTtlSeconds;
        }

        public TokenValidator(float defaultTtlSeconds) : this(SystemClock.Instance, defaultTtlSeconds)
        {
        }

        public TokenValidationResult Validate(TokenLocation? token, MapData? map, DateTimeOffset? currentTime = null, float? ttlSeconds = null)
        {
            if (token == null)
            {
                return TokenValidationResult.Failure(ValidationFailureReason.NullToken, "Token instance is null.");
            }

            if (string.IsNullOrWhiteSpace(token.Id))
            {
                return TokenValidationResult.Failure(ValidationFailureReason.InvalidId, "Token ID is null or whitespace.");
            }

            if (!token.Position.IsFinite())
            {
                return TokenValidationResult.Failure(ValidationFailureReason.InvalidPosition, $"Token position is non-finite: {token.Position}");
            }

            if (map != null && map.Bounds != null)
            {
                if (!map.Bounds.Contains2D(token.Position))
                {
                    return TokenValidationResult.Failure(ValidationFailureReason.OutOfBounds, $"Token position {token.Position} is outside map bounds {map.Bounds}.");
                }
            }

            if (token.IsCollected)
            {
                return TokenValidationResult.Failure(ValidationFailureReason.AlreadyCollected, $"Token {token.Id} is already marked collected.");
            }

            if (token.EstimatedValue < 0)
            {
                return TokenValidationResult.Failure(ValidationFailureReason.NegativeValue, $"Token {token.Id} has negative value: {token.EstimatedValue}.");
            }

            float maxTtl = ttlSeconds ?? DefaultTtlSeconds;
            if (maxTtl > 0)
            {
                var now = currentTime ?? _clock.UtcNow;
                var age = (now - token.LastConfirmedAt).TotalSeconds;
                if (age > maxTtl)
                {
                    return TokenValidationResult.Failure(ValidationFailureReason.StaleToken, $"Token {token.Id} is stale (age: {age:F1}s > TTL: {maxTtl:F1}s).");
                }
            }

            return TokenValidationResult.Success();
        }

        public TokenValidationResult Validate(TokenLocation? token, MapData? map, DateTime? currentTime, float? ttlSeconds = null)
        {
            return Validate(token, map, (DateTimeOffset?)currentTime, ttlSeconds);
        }

        public bool IsValid(TokenLocation? token, MapData? map, DateTimeOffset? currentTime = null, float? ttlSeconds = null)
        {
            return Validate(token, map, currentTime, ttlSeconds).IsValid;
        }

        public bool IsValid(TokenLocation? token, MapData? map, DateTime? currentTime, float? ttlSeconds = null)
        {
            return Validate(token, map, (DateTimeOffset?)currentTime, ttlSeconds).IsValid;
        }
    }
}
