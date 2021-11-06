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
    private GameObject templateUIUser;
    private Transform transformRegisteredUsers;
    private Transform transfromLoggedUsers;
    private Transform transfromConnectedUsers;

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
        templateUIUser = Resources.Load<GameObject>("Prefabs/User");

        userAccounts = new List<UserAccount>();
        connectedUsers = new List<int>();
        loggedUsers = new List<ConnectedAccount>();
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

        if(data[0] == ServerClientSignifiers.Login)
        {
            Debug.Log("start login");
            //check first if the user is already logged in
            if(loggedUsers.Exists(x => x.GetConnectionId() == id))
            {
                SendMessageToClient(ServerStatus.Error+ "," + ServerClientSignifiers.Login + ",User already logged in", id);
                return;
            }
            //check if the user already exist
            if(userAccounts.Exists(x => x.GetName() == data[1]))
            {
                //now check the password
                if(userAccounts.Exists(x => x.GetName() == data[1] && x.GetPassword() == data[2]))
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

}
public static class ServerClientSignifiers
{
    public static string Login = "001";
    public static string Register = "002";
}
public static class ServerStatus
{
    public static string Success = "001";
    public static string Error = "002";
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
