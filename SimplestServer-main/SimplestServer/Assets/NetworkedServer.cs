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
    List<LoggedAccount> loggedUsers;
    private GameObject templateUIUser;
    private Transform transformRegisteredUsers;
    private Transform transfromLoggedUsers;

    // Start is called before the first frame update
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
        transfromLoggedUsers = GameObject.Find("Canvas/UserLoggedIn/Scroll").transform;
        templateUIUser = Resources.Load<GameObject>("Prefabs/User");

        userAccounts = new List<UserAccount>();
        loggedUsers = new List<LoggedAccount>();
        LoadUserAccounts();

    }

    // Update is called once per frame
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
                break;
            case NetworkEventType.DataEvent:
                string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
                ProcessRecievedMsg(msg, recConnectionID);
                break;
            case NetworkEventType.DisconnectEvent:
                Debug.Log("Disconnection, " + recConnectionID);
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
                SendMessageToClient("already logged in", id);
                return;
            }
            //check if the user already exist
            if(userAccounts.Exists(x => x.GetName() == data[1]))
            {
                //now check the password
                if(userAccounts.Exists(x => x.GetName() == data[1] && x.GetPassword() == data[2]))
                {
                    //successfull login
                    SendMessageToClient("successfull login", id);
                    AddNewLoggedUser(userAccounts.Find(x => x.GetName() == data[1]), id);
                }
                else
                {
                    SendMessageToClient("wrong password", id);
                }
            }
            else
            {
                SendMessageToClient("user doesn't exist", id);
            }
        }
        else if (data[0] == ServerClientSignifiers.Register)
        {
            Debug.Log("start regiter");
            //check if the user already exist
            if (userAccounts.Exists(x => x.GetName() == data[1]))
            {
                SendMessageToClient("user already exist, try to login", id);
            }
            else
            {
                AddNewUser(data[1], data[2]);
                SaveUserAccounts();
                SendMessageToClient("new User created", id);
            }
        }

        //check what kind of message you just receive, then decide what to do with it.
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

    private void AddNewLoggedUser(UserAccount user, int connectionId)
    {
        GameObject temp = Instantiate(templateUIUser, transfromLoggedUsers);
        temp.GetComponent<TMPro.TextMeshProUGUI>().text = user.GetName() + "," + connectionId;
        loggedUsers.Add(new LoggedAccount(temp, user, connectionId));
    }

    private void DeleteLoggedUser(int connectionId)
    {
        loggedUsers.Remove(loggedUsers.Find(x => x.GetConnectionId() == connectionId));
    }
}
public static class ServerClientSignifiers
{
    public static string Login = "001";
    public static string Register = "002";
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

public class LoggedAccount
{
    public LoggedAccount(GameObject obj, UserAccount user, int connectionId)
    {
        this.obj = obj;
        this.connectionId = connectionId;
        this.user = user;
    }
    ~LoggedAccount()
    {
        GameObject.Destroy(this.obj);
    }

    private GameObject obj;
    private int connectionId;
    private UserAccount user;
    public int GetConnectionId() { return connectionId; }
    public UserAccount GetUser() { return user; }
    public GameObject GetObject() { return obj; }
}
