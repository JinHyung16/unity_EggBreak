using System.Collections.Generic;
using UnityEngine;
using Nakama;
using System.Threading.Tasks;
using HughGeneric;
using Nakama.TinyJson;
using Cysharp.Threading.Tasks;
using System.Linq;
using Cysharp.Threading.Tasks.CompilerServices;
using System;

sealed class MatchManager : MonoBehaviour
{
    #region Singleton
    private static MatchManager instance;
    public static MatchManager GetInstance
    {
        get
        {
            if (instance == null)
            {
                return null;
            }
            return instance;
        }
    }

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }

        matchCanvasObj.SetActive(true);
        matchCanvas = matchCanvasObj.GetComponent<Canvas>();
        matchCanvas.enabled = true;

        InitMatchManager();
    }
    #endregion
    private string ticket;

    UnityMainThreadDispatcher mainThread;

    private IMatch currentMatch;
    private IUserPresence localUser;
    [HideInInspector] public string localUserSessionID;

    private IDictionary<string, GameObject> playerDictionary;

    [SerializeField] private GameObject LocalPlayer;
    [SerializeField] private GameObject RemotePlayer;
    private GameObject localPlayer;

    public GameObject SpawnPoints;
    [SerializeField] private Transform LocalSpawnPoint;
    [SerializeField] private Transform RemoteSpawnPoint;

    //Match Making UI Canvas
    [SerializeField] private GameObject matchCanvasObj;
    private Canvas matchCanvas;

    private int startTime = 0;

    /// <summary>
    /// ��Ī ���۽� Match���� SetUp�� �����ϰ� ��Ī�� �����ϵ��� �Ѵ�.
    /// </summary>
    public void InitMatchManager()
    {
        playerDictionary = new Dictionary<string, GameObject>();

        GameServer.GetInstance.GetSocket().ReceivedMatchmakerMatched += T => UnityMainThreadDispatcher.GetInstance.Enqueue(() => OnRecivedMatchMakerMatched(T));
        GameServer.GetInstance.GetSocket().ReceivedMatchPresence += T => UnityMainThreadDispatcher.GetInstance.Enqueue(() => OnReceivedMatchPresence(T));
        GameServer.GetInstance.GetSocket().ReceivedMatchState += T => UnityMainThreadDispatcher.GetInstance.Enqueue(async () => await OnReceivedMatchState(T));

        /*
        mainThread = UnityMainThreadDispatcher.Instance();
        GameServer.GetInstance.GetSocket().ReceivedMatchmakerMatched += T => mainThread.Enqueue(() => OnRecivedMatchMakerMatched(T));
        GameServer.GetInstance.GetSocket().ReceivedMatchPresence += T => mainThread.Enqueue(() => OnReceivedMatchPresence(T));
        GameServer.GetInstance.GetSocket().ReceivedMatchState += T => mainThread.Enqueue(async () => await OnReceivedMatchState(T));
        */
    }

    #region Match ã��, ���, ������ �Լ� & Button ���� �Լ�
    /// <summary>
    /// MatchCanvas�� FindMatch Button�� ������ �Լ�
    /// </summary>
    public async void FindMatch()
    {
        await FindMatchmaking();
    }

    /// <summary>
    /// MatchCanvas�� CancelMatch�� ������ �Լ�
    /// </summary>
    public async void CancelMatch()
    {
        await CancelMatchmaking();
    }


    private async UniTask FindMatchmaking(int minPlayer = 2)
    {
        var matchMakingTicket = await GameServer.GetInstance.GetSocket().AddMatchmakerAsync("*", minPlayer);
        ticket = matchMakingTicket.Ticket;
    }

    private async Task CancelMatchmaking()
    {
        if (ticket != null)
        {
            await GameServer.GetInstance.GetSocket().RemoveMatchmakerAsync(ticket);
        }
        matchCanvas.enabled = true;
        SceneController.GetInstance.GoToScene("Lobby").Forget();
    }

    public async UniTask QuickMatch()
    {
        await GameServer.GetInstance.GetSocket().LeaveMatchAsync(currentMatch.Id);

        currentMatch = null;
        localUser = null;

        foreach (var player in playerDictionary.Values)
        {
            Destroy(player);
        }

        playerDictionary.Clear();

        GameManager.GetInstance.GameEnd();
    }
    #endregion


    public void SpawnPlayer(string matchId, IUserPresence user, int spawnIndex = -1)
    {
        if (playerDictionary.ContainsKey(user.SessionId))
        {
            return;
        }

        //spawn Point�� local���� �ƴ����� ���� �ٸ��� �ֱ�
        var spawnPoint = spawnIndex == -1 ?
            SpawnPoints.transform.GetChild(UnityEngine.Random.Range(0, SpawnPoints.transform.childCount - 1)) :
            SpawnPoints.transform.GetChild(spawnIndex);

        var isLocalPlayer = user.SessionId == localUser.SessionId;
        var playerPrefab = isLocalPlayer ? LocalPlayer : RemotePlayer;

        //local�� remote�� ���� ��ġ�� �ٸ���. remote�� ���� ȭ�鿡 ������ �ʴ� ���� ������Ų��
        var player = Instantiate(playerPrefab, spawnPoint.transform.position, Quaternion.identity);

        if (!isLocalPlayer)
        {
            playerPrefab.GetComponent<PlayerNetworkRemoteSync>().NetworkData = new RemotePlayerNetworkData
            {
                MatchId = matchId,
                User = user
            };
        }

        playerDictionary.Add(user.SessionId, player);

        if (isLocalPlayer)
        {
            localPlayer = player;
            MultiplayPresenter.GetInstance.SetOnLocalPlayer(player);
            player.GetComponentInChildren<PlayerDataController>().playerDieEvent.AddListener(OnLocalPlayerDied);
        }

        //match UI Active false�� ����
        matchCanvas.enabled = false;
    }

    /// <summary>
    /// MatchmakerMatched event�� nakama �����κ��� �޾����� ȣ��ȴ�.
    /// </summary>
    /// <param name="matched">The MatchmakerMatched data</param>
    private async void OnRecivedMatchMakerMatched(IMatchmakerMatched matched)
    {
        localUser = matched.Self.Presence;
        localUserSessionID = localUser.SessionId;

        IMatch match = await GameServer.GetInstance.GetSocket().JoinMatchAsync(matched);

        foreach (IUserPresence user in match.Presences)
        {
            SpawnPlayer(match.Id, user);
        }

        currentMatch = match;

        //match canvas�� �ִ� ui���� ����.
        matchCanvas.enabled = false;

        //���� ������Ű��� ������ �˷��ش�
        string jsonData = MatchDataJson.SetStartGame(2);
        SendMatchState(OpCodes.StartGame, jsonData);
    }

    /// <summary>
    /// match�� �����ϰų� �������� ȣ��ȴ�.
    /// </summary>
    /// <param name="matchPresenceEvent">The MatchPresenceEvent data</param>
    private void OnReceivedMatchPresence(IMatchPresenceEvent matchPresenceEvent)
    {
        // ���� ������ ���� ���ֱ�
        foreach (var user in matchPresenceEvent.Joins)
        {
            SpawnPlayer(matchPresenceEvent.MatchId, user);
        }

        // ������ ���� �� �������ֱ�
        foreach (var user in matchPresenceEvent.Leaves)
        {
            if (playerDictionary.ContainsKey(user.SessionId))
            {
                Destroy(playerDictionary[user.SessionId]);
                playerDictionary.Remove(user.SessionId);
            }
        }
    }

    /// <summary>
    /// ������ �����͸� �����Ҷ� �θ���.
    /// </summary>
    /// <param name="matchState"> ��ġ ���¸� ���� </param>
    /// <returns></returns>
    private async UniTask OnReceivedMatchState(IMatchState matchState)
    {
        // local ������ session id ��������
        var userSessionId = matchState.UserPresence.SessionId;

        // match state�� ���̰� �ִٸ� dictionary�� decode���ֱ�
        var state = matchState.State.Length > 0 ? System.Text.Encoding.UTF8.GetString(matchState.State).FromJson<Dictionary<string, string>>() : null;

        // OpCodes�� ���� Match ���� ����
        switch (matchState.OpCode)
        {
            case OpCodes.IsDie:
                var player = playerDictionary[userSessionId];
                Destroy(player, 0.5f);
                playerDictionary.Remove(userSessionId);
                if (playerDictionary.Count == 1 && playerDictionary.First().Key == localUser.SessionId)
                {
                    Debug.Log("�׾����ϴ�");
                    await QuickMatch();
                }
                break;
            case OpCodes.Score:
                MultiplayPresenter.GetInstance.UpdateRemoteScoreInMultiplay(int.Parse(state["Score"]));
                break;
            case OpCodes.StartGame:
                startTime = int.Parse(state["maxTasks"]);
                EnemySpawnCall().Forget();
                break;
        }
    }

    private async void OnLocalPlayerDied(GameObject player)
    {
        await SendMatchStateAsync(OpCodes.IsDie, MatchDataJson.Died(player.transform.position));

        playerDictionary.Remove(localUser.SessionId);
        Destroy(player, 0.5f);
    }
    private async UniTaskVoid EnemySpawnCall()
    {
        await UniTask.Delay(TimeSpan.FromSeconds(startTime), cancellationToken: this.GetCancellationTokenOnDestroy());
        EnemySpawnManager.GetInstance.StartEnemySpawnerPooling();
    }

    public async UniTask SendMatchStateAsync(long opCode, string state)
    {
        await GameServer.GetInstance.GetSocket().SendMatchStateAsync(currentMatch.Id, opCode, state);
    }

    public void SendMatchState(long opCode, string state)
    {
        GameServer.GetInstance.GetSocket().SendMatchStateAsync(currentMatch.Id, opCode, state);
    }

}