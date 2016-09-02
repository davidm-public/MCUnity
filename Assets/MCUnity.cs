/*
    Nefastor : This script relies on Unity networking facilities to make it compatible with both PC and Android, but it
    also uses the .NET framework when it makes life easier without sacrificing compatibility.

    This simple script is a good example of things most network programmers (and not just beginners) often have
    trouble with :

    * determining your own IP address, especially on machines with multiple NIC's, WiFi, etc...
    * identifying other machines on the same network that can be talked with

    I've seen many extremely convoluted solutions to those "problems" over the years when in fact it's not that complicated
    if you bother to RTFM. Unity and .NET, however, make networking simpler than ever.

    Ah, yes, I almost forgot : this script is copyright Nefastor Online, 2016, yada yada, and if you claim you wrote it
    I will personally chase you to the ends of the Earth and force you to listen to Chanel West Coast until
    your brain bleeds. Be nice, give credit where credit's due and we won't have a problem.

    Oh and visit nefastor.com for more informative articles and wise pontification.
*/

/* Modifications to support the ESPinterface / MCUnity project :

   * TO DO : broadcast should stop as soon as an ESP8266 responds (?)

*/

using System.Net;           // for the .NET networking support classes (IP address, networkd endpoint, etc...)
using System.Net.Sockets;   // for the .NET UDP client class, which is as easy to use as it gets
using System.Threading;		// for creating threads to handle network packets in real time
using System.Text;          // for the abstract "Encoding" class, used to convert between bytes and characters
using UnityEngine;          // for obvious reasons



public class MCUnity : MonoBehaviour
{
    public GUIStyle Style = new GUIStyle();     // the display style for Unity IMGUI methods
    public string Display_Buffer;               // a text buffer, to be displayed using IMGUI

    Thread Broadcast_Thread;            // this thread will advertise this machine's presence to any MCU listening
    Thread Receive_Thread;	            // this thread will poll for incoming replies from the network
    public bool Keep_Running = true;    // set to false to tell the threads to self-terminate

    public string My_IP_Address;            // the local IP address
    public string My_Broadcast_Address;     // the broadcast IP address for the local network
    public UdpClient UDP_Socket;            // our UDP socket, basically the entry point into the .NET UDP/IP stack
    public int My_Port = 55555;             // our UDP port, set to an arbitrary value that's easy to type, because laziness

    // public IPAddress Local_IP_Address;      // This app's IP address
    public IPAddress MCU_IP_Address;        // MCU IP address
    public string MCU_IP_Addr;

    // TO DO : REPLACE WITH A MESSAGE CONSISTENT WITH THE PROJECT SPECIFICATIONS
    // I can also try a packet containing various data types to test the ESP's packet parsing functions.
    //public string Broadcast_Message;        // the message this PC will broadcast over the LAN (string)
    public byte[] Broadcast_Payload;        // the same message, converted into a packet payload (byte array)

    /*********************************************************************************************************
    START METHOD

    **********************************************************************************************************/

    // Initialize the script and start the networking threads
    void Start()
    {
        Style.fontSize = 16;                // arbitrary character size. I'm near-sighted. Don't judge me.

        UDP_Socket = new UdpClient(My_Port);             // allocate the UDP socket and bind it to port 55555

        //Broadcast_Payload = Encoding.ASCII.GetBytes(Broadcast_Message);     // conversion from Unicode string into ASCII-format bytes
        // new for ESP, just a test at this point :
        Broadcast_Payload = new byte[1];            // allocate a payload array of one byte
        Broadcast_Payload[0] = 0x01;                // set that byte to UNITY_RX_BROADCAST

        My_IP_Address = Network.player.ipAddress;       // Unity to the rescue !

        // from the local IP address (string) get the individual bytes and combine them into the local broadcast address
        byte[] Bytes = IPAddress.Parse(My_IP_Address).GetAddressBytes();                // use of a static class method
        My_Broadcast_Address = Bytes[0] + "." + Bytes[1] + "." + Bytes[2] + "." + 255;  // host 255 is for broadcasting

        MCU_IP_Addr = "";

        // Initialize remote variables storage
        int_variables_init();

        // this thread will run at very low speed, to do one broadcast every 100 ms
        Broadcast_Thread = new Thread(new ThreadStart(Broadcaster));
        Broadcast_Thread.IsBackground = true;
        Broadcast_Thread.Start();

        // this thread will process any incoming replies to the broadcast, without blocking execution
        Receive_Thread = new Thread(new ThreadStart(Receiver));
        Receive_Thread.IsBackground = true;
        Receive_Thread.Start();
    }

    /*********************************************************************************************************
    GUI

    **********************************************************************************************************/

    // Display the local IP address, the list of all machines on the LAN that run this application, and a refresh button
    void OnGUI()
    {
        GUI.Box(new Rect(0, 0, Screen.width, Style.fontSize), "MCU IP : " + MCU_IP_Addr, Style);

        //if (GUI.Button(new Rect(0, 2 * Style.fontSize, Screen.width, Style.fontSize * 2), "Refresh"))
        //    Display_Buffer = "";        // refresh simply empties the display buffer

        // GUI.Box(new Rect(0, 4 * Style.fontSize, Screen.width, Style.fontSize), Display_Buffer, Style);
    }

    // This method is called when the script exits, which (in this application) coincides with the application ending
    void OnDestroy()
    {
        Keep_Running = false;    // this is vital : it ensures the threads die when the application ends 
    }

    // The broadcast thread is just another method of this class
    private void Broadcaster()
    {
        while (Keep_Running)
        {
            UDP_Socket.Send(Broadcast_Payload, Broadcast_Payload.Length, My_Broadcast_Address, My_Port);
            Thread.Sleep(1000);      // wait 1000 ms until next broadcast, so the network isn't saturated
        }
    }

    /*********************************************************************************************************
    PACKET RECEIVER THREAD - CALLS INDIVIDUAL PARSERS

    **********************************************************************************************************/

    // The nice thing about threads is you can use as many as you need (well, as long as you've got RAM for them)
    private void Receiver()
    {
        while (Keep_Running)
        {
            while (UDP_Socket.Available > 0)  // "if" could also work, since we're inside a "while" already
            {
                IPEndPoint receiveEP = new IPEndPoint(IPAddress.Any, My_Port);    // listen for any IP, on port 55555
                byte[] data = UDP_Socket.Receive(ref receiveEP);     // EP as reference because it'll be populated with remote sender IP and port

                // Store the source address of the incoming packet as the MCU's IP address.
                if (receiveEP.Address.ToString() != My_IP_Address)  // ignore the broadcast message carrying the local IP
                {
                    MCU_IP_Address = receiveEP.Address;
                    MCU_IP_Addr = MCU_IP_Address.ToString();
                }

                // We've got a packet payload form the ESP8266 in the "data" array : call the relevant parser by type
                switch (data[0])
                {
                    case 0x02:         // UNITY_TX_SETUP_INT : setup GUI element for an "int" type variable (32-bit signed)
                        parse_setup_int(data);
                        break;
                    default:
                        break;
                }
            }

            Thread.Sleep(1);    // 1 ms pause necessary to allow the PC's CPU to work at a lower clock speed
        }
    }

    /*********************************************************************************************************
    INTEGER VARIABLES OPERATIONS AND RELATED CODE

    **********************************************************************************************************/

    // "int" variables parameter storage
    private int[] int_variable_value;
    private int[] int_variable_min_val;
    private int[] int_variable_max_val;
    private uint[] int_variable_flags;
    private string[] int_variable_name;
    private int int_variable_occupancy;      // how many "int" variables have been setup ?

    private void int_variables_init()
    {
        int_variable_value = new int[256];      // maximum 256 remote variables of type int
        int_variable_min_val = new int[256];
        int_variable_max_val = new int[256];
        int_variable_flags = new uint[256];
        int_variable_name = new string[256];
        int_variable_occupancy = 0;     // note : zeroing this value amounts to clearing int variable storage
    }

    // UNITY_TX_SETUP_INT
    private void parse_setup_int (byte[] data)
    {
        // A setup function first needs to store the parameters sent by the MCU
        // note : the MCU transmits the index, but in reality this is redundant

        // Converting bytes to int (this matches the endianness of the ESP8266)
        int_variable_value[int_variable_occupancy] = (data[2] << 24) | (data[3] << 16) | (data[4] << 8) | data[5];
        int_variable_min_val[int_variable_occupancy] = (data[6] << 24) | (data[7] << 16) | (data[8] << 8) | data[9];
        int_variable_max_val[int_variable_occupancy] = (data[10] << 24) | (data[11] << 16) | (data[12] << 8) | data[13];
        int_variable_flags[int_variable_occupancy] = (uint)((data[14] << 24) | (data[15] << 16) | (data[16] << 8) | data[17]);

        // extract the variable-length part at the end of a packet 
        byte[] str = new byte[data.Length - 18];
        int i = 18;
        while (i < data.Length)
        {
            str[i - 18] = data[i];
            i++;
        }

        // store it as the variable's display name
        int_variable_name[int_variable_occupancy] = Encoding.UTF8.GetString(str);   // convert the variable length part into a string

        // Build a log string
        string text = "setup int " + data[1]; // data[1] is the common index the ESP8266 and Unity will share to identify the same variable without using its name    
        int i1 = int_variable_value[int_variable_occupancy];
        int i2 = int_variable_min_val[int_variable_occupancy];
        int i3 = int_variable_max_val[int_variable_occupancy];
        uint i4 = int_variable_flags[int_variable_occupancy];
        text += " / " + i1 + " / " + i2 + " / " + i3 + " / " + i4;    
        text += " / " + int_variable_name[int_variable_occupancy];
        print(text);    // log the packet

        // update "occupancy" (max. index)
        int_variable_occupancy++;
    }
}

