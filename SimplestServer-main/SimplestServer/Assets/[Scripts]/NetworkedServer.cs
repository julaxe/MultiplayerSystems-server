using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;

public class NetworkedServer : MonoBehaviour
{
    int maxConnections = 1000;
    int reliableChannelID;
    int unreliableChannelID;
    int hostID;
    int socketPort = 25001;
    List<UserAccount> userAccounts;
    List<int> connectedUsers;
    List<ConnectedAccount> loggedUsers;
    List<ConnectedAccount> inQueueUsers;
    List<GameRoom> _gameRooms;
    private GameObject templateUIUser;
    private Transform transformRegisteredUsers;
    private Transform transfromLoggedUsers;
    private Transform transfromConnectedUsers;
    private Transform transfromInQueueUsers;
    private Transform transfromGameRooms;

    void Start()
    {
        NetworkTransport.Init();

        ConnectionConfig config = new ConnectionConfig();

        reliableChannelID = config.AddChannel(QosType.Reliable);
        unreliableChannelID = config.AddChannel(QosType.Unreliable);
        HostTopology topology = new HostTopology(config, maxConnections);
        hostID = NetworkTransport.AddHost(topology, socketPort, null);

        Debug.Log("Server started, hostid: " + hostID);
        transformRegisteredUsers = GameObject.Find("Canvas/UsersRegistered/Scroll").transform;
        transfromLoggedUsers = GameObject.Find("Canvas/UsersLoggedIn/Scroll").transform;
        transfromConnectedUsers = GameObject.Find("Canvas/UsersConnected/Scroll").transform;
        transfromInQueueUsers = GameObject.Find("Canvas/UsersInQueue/Scroll").transform;
        transfromGameRooms = GameObject.Find("Canvas/GameRooms/Scroll").transform;
        templateUIUser = Resources.Load<GameObject>("Prefabs/User");

        userAccounts = new List<UserAccount>();
        connectedUsers = new List<int>();
        loggedUsers = new List<ConnectedAccount>();
        inQueueUsers = new List<ConnectedAccount>();
        _gameRooms = new List<GameRoom>();
        LoadUserAccounts();

    }

    void Update()
    {

        int recHostID;
        int recConnectionID;
        int recChannelID;
        byte[] recBuffer = new byte[1024];
        int bufferSize = 1024;
        int dataSize;
        byte error = 0;

        NetworkEventType recNetworkEvent = NetworkTransport.Receive(out recHostID, out recConnectionID, out recChannelID, recBuffer, bufferSize, out dataSize, out error);

        switch (recNetworkEvent)
        {
            case NetworkEventType.Nothing:
                break;
            case NetworkEventType.ConnectEvent:
                Debug.Log("Connection, " + recConnectionID);
                AddNewConnectedUser(recConnectionID);
                break;
            case NetworkEventType.DataEvent:
                string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
                ProcessRecievedMsg(msg, recConnectionID);
                break;
            case NetworkEventType.DisconnectEvent:
                Debug.Log("Disconnection, " + recConnectionID);
                DeleteConnectedUser(recConnectionID);
                if(loggedUsers.Exists(x => x.GetConnectionId() == recConnectionID))
                {
                    DeleteLoggedUser(recConnectionID);
                    if(inQueueUsers.Exists(x => x.GetConnectionId() == recConnectionID))
                    {
                        DeleteInQueueUser(recConnectionID);
                    }
                    if(_gameRooms.Exists(x => x.Player1().GetConnectionId() == recConnectionID || x.Player2().GetConnectionId() == recConnectionID))
                    {
                        DeleteGameRoom(recConnectionID);
                    }
                }

                break;
        }

    }
  
    public void SendMessageToClient(string msg, int id)
    {
        byte error = 0;
        byte[] buffer = Encoding.Unicode.GetBytes(msg);
        NetworkTransport.Send(hostID, id, reliableChannelID, buffer, msg.Length * sizeof(char), out error);
    }
    public void SendMessageToGameRoom(string msg, GameRoom room)
    {
        SendMessageToClient(msg, room.Player1().GetConnectionId());
        SendMessageToClient(msg, room.Player2().GetConnectionId());
        if(room.GetSpectatorList().Count > 0)
        {
            foreach(ConnectedAccount user in room.GetSpectatorList())
            {
                SendMessageToClient(msg, user.GetConnectionId());
            }
        }
        
    }
    private void ProcessRecievedMsg(string msg, int id)
    {
        Debug.Log("----From user " + id + ": " + msg);
        string[] data = msg.Split(',');

        if (data[0] == ServerClientSignifiers.Login)
        {
            //check first if the user is already logged in
            if (loggedUsers.Exists(x => x.GetConnectionId() == id))
            {
                SendMessageToClient(ServerStatus.Error + "," + ServerClientSignifiers.Login + ",User already logged in", id);
                return;
            }
            //check if the user already exist
            if (userAccounts.Exists(x => x.GetName() == data[1]))
            {
                //now check the password
                if (userAccounts.Exists(x => x.GetName() == data[1] && x.GetPassword() == data[2]))
                {
                    //successfull login
                    SendMessageToClient(ServerStatus.Success + "," + ServerClientSignifiers.Login + ",Successfully logged in", id);
                    AddNewLoggedUser(userAccounts.Find(x => x.GetName() == data[1]), id);
                }
                else
                {
                    SendMessageToClient(ServerStatus.Error + "," + ServerClientSignifiers.Login + ",Wrong password", id);
                }
            }
            else
            {
                SendMessageToClient(ServerStatus.Error + "," + ServerClientSignifiers.Login + ",User doesn't exist", id);
            }
        }
        else if (data[0] == ServerClientSignifiers.Register)
        {
            Debug.Log("start regiter");
            //check if the user already exist
            if (userAccounts.Exists(x => x.GetName() == data[1]))
            {
                SendMessageToClient(ServerStatus.Error + "," + ServerClientSignifiers.Register + ",User already exist - try to login instead", id);
            }
            else
            {
                AddNewUser(data[1], data[2]);
                AddNewLoggedUser(userAccounts.Find(x => x.GetName() == data[1]), id);
                SaveUserAccounts();
                SendMessageToClient(ServerStatus.Success + "," + ServerClientSignifiers.Register + ",New user created", id);
            }
        }
        else if (data[0] == ServerClientSignifiers.FindMatch)
        {
            Debug.Log("looking for match");
            //add the user to the Queue list.
            AddNewInQueueUser(loggedUsers.Find(x => x.GetConnectionId() == id).GetUser(), id);
            //check if there is another users in the queue list -> if true then match.
            if (inQueueUsers.Count > 1)
            {
                //start match between the 2 first users
                AddGameRoom(inQueueUsers[0], inQueueUsers[1]);

            }
            else
            {
                SendMessageToClient(ServerStatus.Error + "," + ServerClientSignifiers.Message + ",Not enough players to match", id);
            }
            //if not then ->not in match
        }
        else if (data[0] == ServerClientSignifiers.InGame) //disconnect from actual game room
        {
            DeleteGameRoom(id);
        }
        else if (data[0] == ServerClientSignifiers.Board)
        {
            GameRoom temp = _gameRooms.Find(x => x.Player1().GetConnectionId() == id || x.Player2().GetConnectionId() == id);
            if (temp != null)
            {
                temp.UpdateBoard(data[1]);
                SendMessageToGameRoom(ServerStatus.Success + "," + ServerClientSignifiers.Board + ",Board has been updated," + temp.GetBoard(), temp);

                temp.ChangeTurns();
                SendTurnStateToRoom(temp);
                CheckIfPlayerWins(temp);
            }

        }
        else if (data[0] == ServerClientSignifiers.ChatUpdated)
        {
            GameRoom temp = _gameRooms.Find(x => x.Player1().GetConnectionId() == id || x.Player2().GetConnectionId() == id);
            if (temp != null)
            {
                string playerName = id == temp.Player1().GetConnectionId() ? temp.Player1().GetUser().GetName() : temp.Player2().GetUser().GetName();
                string newMessage = playerName + ": " + data[1];
                temp.AddNewMessageToChat(newMessage);
                SendMessageToGameRoom(ServerStatus.Success + "," + ServerClientSignifiers.ChatUpdated + ",Chat succesfully updated," + newMessage, temp);
            }
        }
        else if (data[0] == ServerClientSignifiers.Restart)
        {
            GameRoom temp = _gameRooms.Find(x => x.Player1().GetConnectionId() == id || x.Player2().GetConnectionId() == id);
            if (temp != null)
            {
                temp.UpdateBoard("0 0 0 0 0 0 0 0 0");
                SendMessageToGameRoom(ServerStatus.Success + "," + ServerClientSignifiers.Restart + ",Board reseted", temp);
            }
        }
        else if (data[0] == ServerClientSignifiers.SpectateList)
        {
            if(_gameRooms.Count > 0)
            {
                foreach(GameRoom room in _gameRooms)
                {
                    string roomName = room.Player1().GetUser().GetName() + " vs " + room.Player2().GetUser().GetName();
                    SendMessageToClient(ServerStatus.Success + "," + ServerClientSignifiers.SpectateList + ",Game Room added to the spectate list," + roomName +","+ room.GetRoomId(), id);
                }
            }
        }
        else if (data[0] == ServerClientSignifiers.SpectateGame)
        {
            if (_gameRooms.Count > 0)
            {
                GameRoom room =_gameRooms.Find(x => x.GetRoomId() == int.Parse(data[1]));
                room.AddANewSpectator(loggedUsers.Find(x => x.GetConnectionId() == id));
                SendMessageToClient(ServerStatus.Success + "," + ServerClientSignifiers.SpectateGame + ",Spectating the game," + room.GetBoard(), id);
            }
        }
    }

    private void LoadUserAccounts()
    {
        string path = Application.dataPath + Path.DirectorySeparatorChar + "ListUserAccounts.txt";
        StreamReader sr = new StreamReader(path);
        string line = "";
        if(sr != null)
        {
            while((line = sr.ReadLine()) != null)
            {
                string[] account = line.Split(',');
                AddNewUser(account[0], account[1]);
            }
        }

    }
    private void SaveUserAccounts()
    {
        string path = Application.dataPath + Path.DirectorySeparatorChar + "ListUserAccounts.txt";
        StreamWriter sw = new StreamWriter(path);
        foreach(UserAccount user in userAccounts)
        {
            sw.WriteLine(user.GetName() + "," + user.GetPassword());
        }
        sw.Close();
    }

    private void AddNewUser(string userName, string password)
    {
        userAccounts.Add(new UserAccount(userName, password));
        GameObject temp = Instantiate(templateUIUser, transformRegisteredUsers);
        temp.GetComponent<TMPro.TextMeshProUGUI>().text = userName + "," + password;
    }
    private void AddNewConnectedUser(int connectionId)
    {
        connectedUsers.Add(connectionId);
        transfromConnectedUsers.GetComponent<TMPro.TextMeshProUGUI>().text = "Ids: " + string.Join(",", connectedUsers);
    }

    private void DeleteConnectedUser(int connectionId)
    {
        connectedUsers.Remove(connectionId);
        transfromConnectedUsers.GetComponent<TMPro.TextMeshProUGUI>().text = "Ids: " + string.Join(",", connectedUsers);
    }
    private void AddNewLoggedUser(UserAccount user, int connectionId)
    {
        GameObject temp = Instantiate(templateUIUser, transfromLoggedUsers);
        temp.GetComponent<TMPro.TextMeshProUGUI>().text = user.GetName() + "," + connectionId;
        loggedUsers.Add(new ConnectedAccount(temp, user, connectionId));
    }

    private void DeleteLoggedUser(int connectionId)
    {
        ConnectedAccount temp = loggedUsers.Find(x => x.GetConnectionId() == connectionId);
        Destroy(temp.GetObject());
        loggedUsers.Remove(temp);
    }

    private void AddNewInQueueUser(UserAccount user, int connectionId)
    {
        if(!inQueueUsers.Exists(x => x.GetConnectionId() == connectionId))
        {
            GameObject temp = Instantiate(templateUIUser, transfromInQueueUsers);
            temp.GetComponent<TMPro.TextMeshProUGUI>().text = user.GetName() + "," + connectionId;
            inQueueUsers.Add(new ConnectedAccount(temp, user, connectionId));
        }
    }

    private void DeleteInQueueUser(int connectionId)
    {
        ConnectedAccount temp = inQueueUsers.Find(x => x.GetConnectionId() == connectionId);
        Destroy(temp.GetObject());
        inQueueUsers.Remove(temp);
    }

    private void AddGameRoom(ConnectedAccount p1, ConnectedAccount p2)
    {
        GameObject temp = Instantiate(templateUIUser, transfromGameRooms);
        temp.GetComponent<TMPro.TextMeshProUGUI>().text = p1.GetUser().GetName() + " vs " + p2.GetUser().GetName();
        GameRoom newRoom = new GameRoom(temp, p1, p2);
        _gameRooms.Add(newRoom);
        
        SendMessageToClient(ServerStatus.Success + "," + ServerClientSignifiers.FindMatch + ",Match Found,Player1," + p2.GetUser().GetName() + "," + newRoom.GetRoomId(), p1.GetConnectionId());
        SendMessageToClient(ServerStatus.Success + "," + ServerClientSignifiers.FindMatch + ",Match Found,Player2," + p1.GetUser().GetName() + "," + newRoom.GetRoomId(), p2.GetConnectionId());
        
        SendTurnStateToRoom(newRoom); // the room that we just added.
        DeleteInQueueUser(p1.GetConnectionId());
        DeleteInQueueUser(p2.GetConnectionId());
    }

    private void DeleteGameRoom(int connectionId)
    {
        GameRoom temp = _gameRooms.Find(x => (x.Player1().GetConnectionId() == connectionId || x.Player2().GetConnectionId() == connectionId));
        if(temp == null)
        {
            //just in queue
            DeleteInQueueUser(connectionId);
            return;
        }
        Destroy(temp.GetObject());
        if(temp.Player1().GetConnectionId() == connectionId)
        {
            SendMessageToClient(ServerStatus.Error + "," + ServerClientSignifiers.FindMatch + "," + "The other player has disconnected", temp.Player2().GetConnectionId());
            SendMessageToClient(ServerStatus.Error + "," + ServerClientSignifiers.FindMatch + "," + "disconnected from the actual game room", temp.Player1().GetConnectionId());
        }
        else
        {
            SendMessageToClient(ServerStatus.Error + "," + ServerClientSignifiers.FindMatch + "," + "The other player has disconnected", temp.Player1().GetConnectionId());
            SendMessageToClient(ServerStatus.Error + "," + ServerClientSignifiers.FindMatch + "," + "disconnected from the actual game room", temp.Player2().GetConnectionId());
        }
        _gameRooms.Remove(temp);
    }

    private void SendTurnStateToRoom(GameRoom gameRoom)
    {
        if (gameRoom.PlayerTurn()[0])
        {
            SendMessageToClient(ServerStatus.Success + "," + ServerClientSignifiers.InGame + ",Is your turn", gameRoom.Player1().GetConnectionId());
            SendMessageToClient(ServerStatus.Error + "," + ServerClientSignifiers.InGame + ",Enemy's turn", gameRoom.Player2().GetConnectionId());
        }
        else
        {
            SendMessageToClient(ServerStatus.Success + "," + ServerClientSignifiers.InGame + ",Is your turn", gameRoom.Player2().GetConnectionId());
            SendMessageToClient(ServerStatus.Error + "," + ServerClientSignifiers.InGame + ",Enemy's turn", gameRoom.Player1().GetConnectionId());
        }
    }

    private bool CheckIfPlayerWins(GameRoom gameRoom)
    {
        string[] board = gameRoom.GetBoard().Split(' ');

        //check if player 1 wins.
        string player1Mark = "1";
        string player2Mark = "2";
        if(CheckBoardWinningState(player1Mark, board))
        {
            SendMessageToClient(ServerStatus.Success + "," + ServerClientSignifiers.PlayerWin + ",You Win!", gameRoom.Player1().GetConnectionId());
            SendMessageToClient(ServerStatus.Error + "," + ServerClientSignifiers.PlayerWin + ",You Lose!", gameRoom.Player2().GetConnectionId());
            return true;
        }
        if (CheckBoardWinningState(player2Mark, board))
        {
            SendMessageToClient(ServerStatus.Success + "," + ServerClientSignifiers.PlayerWin + ",You Win!", gameRoom.Player2().GetConnectionId());
            SendMessageToClient(ServerStatus.Error + "," + ServerClientSignifiers.PlayerWin + ",You Lose!", gameRoom.Player1().GetConnectionId());
            return true;
        }

        return false;
    }

    private bool CheckBoardWinningState(string playerMark, string[] board)
    {
        if ((board[0] == board[1] && board[1] == board[2] && board[2] == playerMark) || //top row
            (board[3] == board[4] && board[4] == board[5] && board[5] == playerMark) || //middle row
            (board[6] == board[7] && board[7] == board[8] && board[8] == playerMark) || //bottom row
            (board[0] == board[3] && board[3] == board[6] && board[6] == playerMark) || // left column
            (board[1] == board[4] && board[4] == board[7] && board[7] == playerMark) || // middle column
            (board[2] == board[5] && board[5] == board[8] && board[8] == playerMark) || // right column
            (board[0] == board[4] && board[4] == board[8] && board[8] == playerMark) || //first cross
            (board[2] == board[4] && board[4] == board[6] && board[6] == playerMark)  //second cross
            )
        {
            return true;
        }

        return false;
    }
}

public class UserAccount
{
    public UserAccount(string name, string password)
    {
        this.name = name;
        this.password = password;
    }
    private string name;
    private string password;

    public string GetName() { return name; }
    public string GetPassword() { return password; }

}

public class ConnectedAccount
{
    public ConnectedAccount(GameObject obj, UserAccount user, int connectionId)
    {
        this.obj = obj;
        this.connectionId = connectionId;
        this.user = user;
    }

    private GameObject obj;
    private int connectionId;
    private UserAccount user;
    public int GetConnectionId() { return connectionId; }
    public UserAccount GetUser() { return user; }
    public GameObject GetObject() { return obj; }
}
public class GameRoom
{
    public GameRoom(GameObject obj, ConnectedAccount account1, ConnectedAccount account2)
    {
        _obj = obj;
        _player1 = account1;
        _player2 = account2;
        _playerTurn = new bool[2]; //only 2 players
        _playerTurn[0] = Random.Range(0, 2) == 1;
        _playerTurn[1] = !_playerTurn[0];
        _board = "0 0 0 0 0 0 0 0 0"; //empty board
        _roomId = id++;
        _chat = new List<string>();
        _spectatorList = new List<ConnectedAccount>();
    }
    private ConnectedAccount _player1;
    private ConnectedAccount _player2;
    private bool[] _playerTurn;
    private GameObject _obj;
    private string _board;
    private int _roomId;
    private List<string> _chat;
    private List<ConnectedAccount> _spectatorList;

    public static int id;

    public GameObject GetObject() { return _obj; }
    public ConnectedAccount Player1() { return _player1; }
    public ConnectedAccount Player2() { return _player2; }

    public bool[] PlayerTurn() { return _playerTurn; }

    public string GetBoard() { return _board; }

    public int GetRoomId() { return _roomId; }
    public void UpdateBoard(string board) { _board = board; }

    public void AddNewMessageToChat(string newMessage)
    {
        _chat.Add(newMessage);
    }
    public List<string> GetChat() { return _chat; }

    public void ChangeTurns()
    {
        _playerTurn[0] = _playerTurn[1];
        _playerTurn[1] = !_playerTurn[0];
    }

    public void AddANewSpectator(ConnectedAccount user)
    {
        _spectatorList.Add(user);
    }
    public void DeleteASpectator(ConnectedAccount user)
    {
        _spectatorList.Remove(user);
    }
    public List<ConnectedAccount> GetSpectatorList() { return _spectatorList; }
}

public static class ServerClientSignifiers
{
    public static string Message = "000";
    public static string Login = "001";
    public static string Register = "002";
    public static string FindMatch = "003";
    public static string InGame = "004";
    public static string Board = "005";
    public static string PlayerWin = "006";
    public static string Restart = "007";
    public static string ChatUpdated = "008";
    public static string SpectateList = "009";
    public static string SpectateGame = "010";
    public static string MatchHistory = "011";
    public static string ReplaySystem = "012";
}
public static class ServerStatus
{
    public static string Success = "001";
    public static string Error = "002";
}
