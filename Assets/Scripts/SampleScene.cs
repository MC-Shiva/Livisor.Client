using System;
using Livisor;
using UnityEngine;

public class SampleScene : MonoBehaviour
{
    public string serverAddress = ConnectivityCheck.DefaultServerAddress;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    async void Start()
    {
        try
        {
            var result = await ConnectivityCheck.CheckServerAsync(serverAddress, destroyCancellationToken);
            Debug.Log($"[Livisor] Server 疎通確認成功: {serverAddress}, 100 + 200 = {result}");
        }
        catch (OperationCanceledException) when (destroyCancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }
}
