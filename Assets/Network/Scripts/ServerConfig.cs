using UnityEngine;

[CreateAssetMenu(fileName = "ServerConfig", menuName = "Livisor/ServerConfig")]
public class ServerConfig : ScriptableObject
{
    [SerializeField] private string _serverAddress = Livisor.ConnectivityCheck.DefaultServerAddress;

    public string ServerAddress => _serverAddress;
}
