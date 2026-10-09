using System;
using Assistant.Core.Data;

namespace Assistant.Core.Abstractions
{
    public interface IGameApi
    {
        PlayerState GetPlayerState();
        event Action<TokenLocation>? TokenSpawned;
        event Action<string>? TokenCollected;
        event Action<string>? TokenRemoved;
        event Action<MapChangedEventArgs>? MapChanged;
    }
}
