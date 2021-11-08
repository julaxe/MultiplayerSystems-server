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

    private void ProcessRecievedMsg(string msg, int id)
    {
        string[] data = msg.Split(',');

        if (data[0] == ServerClientSignifiers.Login)
        {
            Debug.Log("start login");
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
            if(inQueueUsers.Count > 1)
            {
                //start match between the 2 first users
                foreach(var user in inQueueUsers)
                {
                    SendMessageToClient(ServerStatus.Success + "," + ServerClientSignifiers.FindMatch + ",Match Found", user.GetConnectionId());
                }
                AddGameRoom(inQueueUsers[0], inQueueUsers[1]);
            }
            else
            {
                SendMessageToClient(ServerStatus.Error + "," + ServerClientSignifiers.FindMatch + ",Not enough players to match", id);
            }
            //if not then ->not in match
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
        _gameRooms.Add(new GameRoom(temp, p1, p2));
        DeleteInQueueUser(p1.GetConnectionId());
        DeleteInQueueUser(p2.GetConnectionId());
    }

    private void DeleteGameRoom(ConnectedAccount any)
    {
        GameRoom temp = _gameRooms.Find(x => (x.Player1() == any || x.Player2() == any));
        Destroy(temp.GetObject());
        _gameRooms.Remove(temp);
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
    }
    private ConnectedAccount _player1;
    private ConnectedAccount _player2;
    private GameObject _obj;

    public GameObject GetObject() { return _obj; }
    public ConnectedAccount Player1() { return _player1; }
    public ConnectedAccount Player2() { return _player2; }
}

public static class ServerClientSignifiers
{
    public static string Login = "001";
    public static string Register = "002";
    public static string FindMatch = "003";
}
public static class ServerStatus
{
    public static string Success = "001";
    public static string Error = "002";
}
