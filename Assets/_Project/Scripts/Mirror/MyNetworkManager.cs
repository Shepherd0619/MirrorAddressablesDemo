// MyNetworkManager
// Shepherd Zhu
// Provide Addressables Scene Managemnt support.
using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Mirror;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;

/*
	Documentation: https://mirror-networking.gitbook.io/docs/components/network-manager
	API Reference: https://mirror-networking.com/docs/api/Mirror.NetworkManager.html
*/

public class MyNetworkManager : NetworkManager
{
	// Overrides the base singleton so we don't
	// have to cast to this type everywhere.
	public static new MyNetworkManager singleton { get; private set; }

	public static new AsyncOperationHandle<SceneInstance> loadingSceneAsync;

	/// <summary>
	/// Runs on both Server and Client
	/// Networking is NOT initialized when this fires
	/// </summary>
	public override void Awake()
	{
		base.Awake();
		singleton = this;
	}

	#region Unity Callbacks

	public override void OnValidate()
	{
		base.OnValidate();
	}

	/// <summary>
	/// Runs on both Server and Client
	/// Networking is NOT initialized when this fires
	/// </summary>
	public override void Start()
	{
		base.Start();
	}

	/// <summary>
	/// Runs on both Server and Client
	/// </summary>
	public override void LateUpdate()
	{
		base.LateUpdate();

		UpdateScene();
	}

	/// <summary>
	/// Runs on both Server and Client
	/// </summary>
	public override void OnDestroy()
	{
		base.OnDestroy();
	}

	public override void Update()
	{
		base.Update();
	}

	string sceneName;

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 120, 300, 9999));

        if (NetworkServer.active)
        {
            sceneName = GUILayout.TextField(sceneName);
            if(GUILayout.Button("Change Scene"))
			{
				ServerChangeScene(sceneName);
			}
        }

        GUILayout.EndArea();
    }

    #endregion

    #region Start & Stop

    /// <summary>
    /// Set the frame rate for a headless server.
    /// <para>Override if you wish to disable the behavior or set your own tick rate.</para>
    /// </summary>
    public override void ConfigureHeadlessFrameRate()
	{
		base.ConfigureHeadlessFrameRate();
	}

	/// <summary>
	/// called when quitting the application by closing the window / pressing stop in the editor
	/// </summary>
	public override void OnApplicationQuit()
	{
		base.OnApplicationQuit();
	}

	#endregion

	#region Scene Management

	/// <summary>
	/// This causes the server to switch scenes and sets the networkSceneName.
	/// <para>Clients that connect to this server will automatically switch to this scene. This is called automatically if onlineScene or offlineScene are set, but it can be called from user code to switch scenes again while the game is in progress. This automatically sets clients to be not-ready. The clients must call NetworkClient.Ready() again to participate in the new scene.</para>
	/// </summary>
	/// <param name="newSceneName"></param>
	public override void ServerChangeScene(string newSceneName)
	{
		if (string.IsNullOrWhiteSpace(newSceneName))
		{
			Debug.LogError("ServerChangeScene empty scene name");
			return;
		}

		if (NetworkServer.isLoadingScene && newSceneName == networkSceneName)
		{
			Debug.LogError($"Scene change is already in progress for {newSceneName}");
			return;
		}

		// Debug.Log($"ServerChangeScene {newSceneName}");
		NetworkServer.SetAllClientsNotReady();
		networkSceneName = newSceneName;

		// Let server prepare for scene change
		OnServerChangeScene(newSceneName);

		// set server flag to stop processing messages while changing scenes
		// it will be re-enabled in FinishLoadScene.
		NetworkServer.isLoadingScene = true;

		// loadingSceneAsync = SceneManager.LoadSceneAsync(newSceneName);
		loadingSceneAsync = Addressables.LoadSceneAsync(newSceneName);

		// ServerChangeScene can be called when stopping the server
		// when this happens the server is not active so does not need to tell clients about the change
		if (NetworkServer.active)
		{
			// notify all clients about the new scene
			NetworkServer.SendToAll(new SceneMessage
			{
				sceneName = newSceneName
			});
		}

		startPositionIndex = 0;
		startPositions.Clear();
	}

	/// <summary>
	/// Called from ServerChangeScene immediately before SceneManager.LoadSceneAsync is executed
	/// <para>This allows server to do work / cleanup / prep before the scene changes.</para>
	/// </summary>
	/// <param name="newSceneName">Name of the scene that's about to be loaded</param>
	public override void OnServerChangeScene(string newSceneName) { }

	/// <summary>
	/// Called on the server when a scene is completed loaded, when the scene load was initiated by the server with ServerChangeScene().
	/// </summary>
	/// <param name="sceneName">The name of the new scene.</param>
	public override void OnServerSceneChanged(string sceneName) { }

	/// <summary>
	/// Called from ClientChangeScene immediately before SceneManager.LoadSceneAsync is executed
	/// <para>This allows client to do work / cleanup / prep before the scene changes.</para>
	/// </summary>
	/// <param name="newSceneName">Name of the scene that's about to be loaded</param>
	/// <param name="sceneOperation">Scene operation that's about to happen</param>
	/// <param name="customHandling">true to indicate that scene loading will be handled through overrides</param>
	public override void OnClientChangeScene(string newSceneName, SceneOperation sceneOperation, bool customHandling) { }

	/// <summary>Called on clients when a scene has completed loaded, when the scene load was initiated by the server.</summary>
	// Scene changes can cause player objects to be destroyed. The default
	// implementation of OnClientSceneChanged in the NetworkManager is to
	// add a player object for the connection if no player object exists.
	public override void OnClientSceneChanged()
	{
		// always become ready.
		if (NetworkClient.connection.isAuthenticated && !NetworkClient.ready) NetworkClient.Ready();

		// Only call AddPlayer for normal scene changes, not additive load/unload
		if (NetworkClient.connection.isAuthenticated && clientAddressableSceneOperation == SceneOperation.Normal && autoCreatePlayer && NetworkClient.localPlayer == null)
		{
			// add player if existing one is null

			// fix: 修复玩家加入报There is already a player for this connection错误。
			if (loadingSceneAsync.IsValid() && loadingSceneAsync.IsDone && loadingSceneAsync.Status == AsyncOperationStatus.Succeeded) 
			{
				NetworkClient.AddPlayer();
				//Debug.LogWarning("AddPlayer!");
			}
		}
	}

	void OnClientSceneInternal(SceneMessage msg)
	{
		//Debug.Log("NetworkManager.OnClientSceneInternal");

		// This needs to run for host client too. NetworkServer.active is checked there
		if (NetworkClient.isConnected)
		{
			ClientChangeScene(msg.sceneName, msg.sceneOperation, msg.customHandling);
		}
	}

	// This is only set in ClientChangeScene below...never on server.
	// We need to check this in OnClientSceneChanged called from FinishLoadSceneClientOnly
	// to prevent AddPlayer message after loading/unloading additive scenes
	SceneOperation clientAddressableSceneOperation = SceneOperation.Normal;

	internal void ClientChangeScene(string newSceneName, SceneOperation sceneOperation = SceneOperation.Normal, bool customHandling = false)
	{
		if (string.IsNullOrWhiteSpace(newSceneName))
		{
			Debug.LogError("ClientChangeScene empty scene name");
			return;
		}

		//Debug.Log($"ClientChangeScene newSceneName: {newSceneName} networkSceneName{networkSceneName}");

		// Let client prepare for scene change
		OnClientChangeScene(newSceneName, sceneOperation, customHandling);

		// After calling OnClientChangeScene, exit if server since server is already doing
		// the actual scene change, and we don't need to do it for the host client
		if (NetworkServer.active)
			return;

		// set client flag to stop processing messages while loading scenes.
		// otherwise we would process messages and then lose all the state
		// as soon as the load is finishing, causing all kinds of bugs
		// because of missing state.
		// (client may be null after StopClient etc.)
		// Debug.Log("ClientChangeScene: pausing handlers while scene is loading to avoid data loss after scene was loaded.");
		NetworkClient.isLoadingScene = true;

		// Cache sceneOperation so we know what was requested by the
		// Scene message in OnClientChangeScene and OnClientSceneChanged
		clientAddressableSceneOperation = sceneOperation;

		// scene handling will happen in overrides of OnClientChangeScene and/or OnClientSceneChanged
		// Do not call FinishLoadScene here. Custom handler will assign loadingSceneAsync and we need
		// to wait for that to finish. UpdateScene already checks for that to be not null and isDone.
		if (customHandling)
			return;

		switch (sceneOperation)
		{
			case SceneOperation.Normal:
				loadingSceneAsync = Addressables.LoadSceneAsync(newSceneName);
				break;
			case SceneOperation.LoadAdditive:
				// Ensure additive scene is not already loaded on client by name or path
				// since we don't know which was passed in the Scene message
				if (!SceneManager.GetSceneByName(newSceneName).IsValid() && !SceneManager.GetSceneByPath(newSceneName).IsValid())
					// loadingSceneAsync = SceneManager.LoadSceneAsync(newSceneName, LoadSceneMode.Additive);
					loadingSceneAsync = Addressables.LoadSceneAsync(newSceneName, LoadSceneMode.Additive);
				else
				{
					Debug.LogWarning($"Scene {newSceneName} is already loaded");

					// Reset the flag that we disabled before entering this switch
					NetworkClient.isLoadingScene = false;
				}
				break;
			case SceneOperation.UnloadAdditive:
				// Ensure additive scene is actually loaded on client by name or path
				// since we don't know which was passed in the Scene message
				if (SceneManager.GetSceneByName(newSceneName).IsValid() || SceneManager.GetSceneByPath(newSceneName).IsValid())
					// loadingSceneAsync = SceneManager.UnloadSceneAsync(newSceneName, UnloadSceneOptions.UnloadAllEmbeddedSceneObjects);
					loadingSceneAsync = Addressables.UnloadSceneAsync(loadingSceneAsync, UnloadSceneOptions.UnloadAllEmbeddedSceneObjects);
				else
				{
					Debug.LogWarning($"Cannot unload {newSceneName} with UnloadAdditive operation");

					// Reset the flag that we disabled before entering this switch
					NetworkClient.isLoadingScene = false;
				}
				break;
		}

		// don't change the client's current networkSceneName when loading additive scene content
		if (sceneOperation == SceneOperation.Normal)
			networkSceneName = newSceneName;
	}

	void UpdateScene()
	{
		if (loadingSceneAsync.IsValid() && loadingSceneAsync.IsDone && loadingSceneAsync.Status == AsyncOperationStatus.Succeeded)
		{
			//Debug.Log($"ClientChangeScene done readyConn {clientReadyConnection}");

			// try-finally to guarantee loadingSceneAsync being cleared.
			// fixes https://github.com/vis2k/Mirror/issues/2517 where if
			// FinishLoadScene throws an exception, loadingSceneAsync would
			// never be cleared and this code would run every Update.
			try
			{
				FinishLoadScene();

				// fix: 加载完场景灯光没激活
				if (!string.IsNullOrWhiteSpace(networkSceneName))
				{
					Scene scene = SceneManager.GetSceneByName(networkSceneName);
					if (scene != null && SceneManager.GetActiveScene().name != networkSceneName)
						SceneManager.SetActiveScene(scene);
				}

                // fix: 修复玩家加入报There is already a player for this connection错误。
                loadingSceneAsync = default;
			}
			catch (Exception e)
			{
				Debug.LogError($"[{GetType().Name}] Error when FinishLoadScene! \n{e}");
			}
		}
	}
	#endregion

	#region Server System Callbacks

	/// <summary>
	/// Called on the server when a new client connects.
	/// <para>Unity calls this on the Server when a Client connects to the Server. Use an override to tell the NetworkManager what to do when a client connects to the server.</para>
	/// </summary>
	/// <param name="conn">Connection from client.</param>
	public override void OnServerConnect(NetworkConnectionToClient conn) { }

	/// <summary>
	/// Called on the server when a client is ready.
	/// <para>The default implementation of this function calls NetworkServer.SetClientReady() to continue the network setup process.</para>
	/// </summary>
	/// <param name="conn">Connection from client.</param>
	public override void OnServerReady(NetworkConnectionToClient conn)
	{
		base.OnServerReady(conn);
	}

	/// <summary>
	/// Called on the server when a client adds a new player with ClientScene.AddPlayer.
	/// <para>The default implementation for this function creates a new player object from the playerPrefab.</para>
	/// </summary>
	/// <param name="conn">Connection from client.</param>
	public override void OnServerAddPlayer(NetworkConnectionToClient conn)
	{
		base.OnServerAddPlayer(conn);
	}

	/// <summary>
	/// Called on the server when a client disconnects.
	/// <para>This is called on the Server when a Client disconnects from the Server. Use an override to decide what should happen when a disconnection is detected.</para>
	/// </summary>
	/// <param name="conn">Connection from client.</param>
	public override void OnServerDisconnect(NetworkConnectionToClient conn)
	{
		base.OnServerDisconnect(conn);
	}

	/// <summary>
	/// Called on server when transport raises an error.
	/// <para>NetworkConnection may be null.</para>
	/// </summary>
	/// <param name="conn">Connection of the client...may be null</param>
	/// <param name="transportError">TransportError enum</param>
	/// <param name="message">String message of the error.</param>
	public override void OnServerError(NetworkConnectionToClient conn, TransportError transportError, string message) { }

	#endregion

	#region Client System Callbacks

	/// <summary>
	/// Called on the client when connected to a server.
	/// <para>The default implementation of this function sets the client as ready and adds a player. Override the function to dictate what happens when the client connects.</para>
	/// </summary>
	public override void OnClientConnect()
	{
		base.OnClientConnect();
	}

	/// <summary>
	/// Called on clients when disconnected from a server.
	/// <para>This is called on the client when it disconnects from the server. Override this function to decide what happens when the client disconnects.</para>
	/// </summary>
	public override void OnClientDisconnect() { }

	/// <summary>
	/// Called on clients when a servers tells the client it is no longer ready.
	/// <para>This is commonly used when switching scenes.</para>
	/// </summary>
	public override void OnClientNotReady() { }

	/// <summary>
	/// Called on client when transport raises an error.</summary>
	/// </summary>
	/// <param name="transportError">TransportError enum.</param>
	/// <param name="message">String message of the error.</param>
	public override void OnClientError(TransportError transportError, string message) { }

	#endregion

	#region Start & Stop Callbacks

	// Since there are multiple versions of StartServer, StartClient and StartHost, to reliably customize
	// their functionality, users would need override all the versions. Instead these callbacks are invoked
	// from all versions, so users only need to implement this one case.

	/// <summary>
	/// This is invoked when a host is started.
	/// <para>StartHost has multiple signatures, but they all cause this hook to be called.</para>
	/// </summary>
	public override void OnStartHost() { }

	/// <summary>
	/// This is invoked when a server is started - including when a host is started.
	/// <para>StartServer has multiple signatures, but they all cause this hook to be called.</para>
	/// </summary>
	public override void OnStartServer() { }

	/// <summary>
	/// This is invoked when the client is started.
	/// </summary>
	public override void OnStartClient()
	{
		// 注册Addressable专用加载场景。
		NetworkClient.UnregisterHandler<SceneMessage>();
		NetworkClient.RegisterHandler<SceneMessage>(OnClientSceneInternal, false);
	}

	/// <summary>
	/// This is called when a host is stopped.
	/// </summary>
	public override void OnStopHost() { }

	/// <summary>
	/// This is called when a server is stopped - including when a host is stopped.
	/// </summary>
	public override void OnStopServer() { }

	/// <summary>
	/// This is called when a client is stopped.
	/// </summary>
	public override void OnStopClient() { }

	#endregion
}
