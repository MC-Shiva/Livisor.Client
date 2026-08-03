using UnityEngine;

[CreateAssetMenu(fileName = "ServerConfig", menuName = "Livisor/ServerConfig")]
public class ServerConfig : ScriptableObject
{
    [SerializeField] private string _serverAddress = "http://localhost:5210";

    public string ServerAddress => _serverAddress;
}