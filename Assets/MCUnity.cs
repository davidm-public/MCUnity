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
        firmware_function_init();

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

    TO DO :

        *   this guy offers very limited layout customization options
        *   start implementing "tile" concept for GUI elements

    **********************************************************************************************************/

    // Display the local IP address, the list of all machines on the LAN that run this application, and a refresh button
    void OnGUI()
    {
        int line = 0;

        GUI.Box(new Rect(0, line, Screen.width / 2, Style.fontSize), "MCU IP : " + MCU_IP_Addr, Style);

        // button to request the MCU perform its MCUnity setup operations sequence again
        if (GUI.Button(new Rect(Screen.width / 2, line, Screen.width / 2, Style.fontSize), "Force Setup"))
            force_setup();

        line += Style.fontSize * 2;

        //if (GUI.Button(new Rect(0, 2 * Style.fontSize, Screen.width, Style.fontSize * 2), "Refresh"))
        //    Display_Buffer = "";        // refresh simply empties the display buffer

        // GUI.Box(new Rect(0, 4 * Style.fontSize, Screen.width, Style.fontSize), Display_Buffer, Style);

        int tile_height = Style.fontSize * 4;
        int tile_width = Screen.width / 2;
        int tile_xpos = 0;
        int tile_ypos = line;
        int k;

        // generate GUI "tiles" for each "int" type variable
        for (k = 0; k < int_variable_occupancy; k++ , tile_ypos += tile_height)
        {
            // create GUI box (tile) for the variable
            GUI.Box(new Rect(tile_xpos, tile_ypos, tile_width, tile_height), int_variable_name[k], Style);
            // display the variable's current value
            GUI.Label(new Rect(tile_xpos, tile_ypos + Style.fontSize, tile_width, Style.fontSize), int_variable_value[k].ToString(), Style);
            // add a user input field
            int_user_input[k] = GUI.TextField(new Rect(tile_xpos, tile_ypos + (2 * Style.fontSize), tile_width / 2, Style.fontSize * 2), int_user_input[k]);
            // add a button to set the variable
            if (GUI.Button(new Rect(tile_xpos + (tile_width / 2), tile_ypos + (2 * Style.fontSize), tile_width / 2, Style.fontSize * 2), "Set"))
                set_int(k);
        }

        // generate GUI "tiles" for each firmware function that can be called from the GUI
        tile_height = Style.fontSize * 2;
        for (k = 0; k < firmware_function_occupancy; k++, tile_ypos += tile_height)
        {
            if (GUI.Button(new Rect(tile_xpos, tile_ypos + tile_height, tile_width, tile_height), firmware_function_name[k]))
                firmware_function_call(k);
        }

    }

    /*********************************************************************************************************
    DESTRUCTOR

    **********************************************************************************************************/

    // This method is called when the script exits, which (in this application) coincides with the application ending
    void OnDestroy()
    {
        Keep_Running = false;    // this is vital : it ensures the threads die when the application ends 
    }

    /*********************************************************************************************************
    BROADCASTER THREAD, LETTING MCU'S KNOW THE APP IS LIVE AND WHAT IP ADDRESS IT IS AT

    **********************************************************************************************************/

    // The broadcast thread is just another method of this class
    private void Broadcaster()
    {
        while (Keep_Running)
        {
            UDP_Socket.Send(Broadcast_Payload, Broadcast_Payload.Length, My_Broadcast_Address, My_Port);
            Thread.Sleep(1000);      // wait 1000 ms until next broadcast, so the network isn't saturated
        }
    }

    // UNITY_RX_FORCE_SETUP - Request the MCU perform its MCUnity setup operations sequence again
    private void force_setup()
    {
        // Reset local storate of GUI setup
        int_variables_init();
        firmware_function_init();

        // Create the operation's packet
        byte[] payload = new byte[1];   // packet only contains a command byte
        payload[0] = 0x02;              // UNITY_RX_FORCE_SETUP
        UDP_Socket.Send(payload, payload.Length, MCU_IP_Addr, My_Port);
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
                    case 0x00:          // UNITY_TX_SETUP_FUNCTION : setup a firmware function GUI element
                        parse_setup_function(data);
                        break;
                    case 0x04:          // UNITY_TX_SETUP_INT : setup GUI element for an "int" type variable (32-bit signed)
                        parse_setup_int(data);
                        break;
                    case 0x06:          // UNITY_TX_UPDATE_INT : update the value of "int" type variable(s)
                        parse_update_int(data);
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
    private string[] int_user_input;        // to be used by the GUI
    private int int_variable_occupancy;      // how many "int" variables have been setup ?

    private void int_variables_init()
    {
        int_variable_value = new int[256];      // maximum 256 remote variables of type int
        int_variable_min_val = new int[256];
        int_variable_max_val = new int[256];
        int_variable_flags = new uint[256];
        int_variable_name = new string[256];
        int_user_input = new string[256];
        int_variable_occupancy = 0;     // note : zeroing this value amounts to clearing int variable storage
    }

    // UNITY_TX_SETUP_INT
    private void parse_setup_int(byte[] data)
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

        // initialize user input field
        int_user_input[int_variable_occupancy] = "";

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

    // UNITY_TX_UPDATE_INT
    private void parse_update_int(byte[] data)
    {
        // initialize log string
        string log = "update (int) from offset ";
        // get the index for the first variable contained in the packet
        int offset = (int)data[1];  // data[0] is the packet's command byte, we've already decoded that, which is how we got here
        log += offset + " : ";
        // compute how many variables are contained in the packet
        int length = data.GetLength(0) - 2;     // how many bytes in the packet, exclusing command and offset bytes ?
        length = length / 4;          // Now divide by 4 bytes to get the number of "int" values
        // get those values from packet to variables:
        int j,k;        // j is the variable index, k is the 
        for (j=0, k = 2; j < length; j++, k+=4) 
        {
            int value = (data[k] << 24) | (data[k + 1] << 16) | (data[k + 2] << 8) | data[k + 3];
            int_variable_value[j + offset] = value;
            log += value + "  ";
        }
        // log
        print(log);
    }

    // UNITY_RX_SET_INT - Remotely set one of the "int" variables
    private void set_int (int index)
    {
        // Create the operation's packet
        byte[] payload = new byte[6];   // packet is 6 bytes long : command byte, variable's index byte, variable's value (32-bit)
        payload[0] = 0x05;              // UNITY_RX_SET_INT
        payload[1] = (byte) index;
 
        int value;
        if (int.TryParse(int_user_input[index], out value))  // Convert the value from string to integer
        { // successful parsing
            payload[2] = (byte)((value >> 24) & 0xFF);       // Value's MSB
            payload[3] = (byte)((value >> 16) & 0xFF);
            payload[4] = (byte)((value >> 8) & 0xFF);
            payload[5] = (byte)(value & 0xFF);               // Value's LSB
                                                             // Send the packet to the MCU
            UDP_Socket.Send(payload, payload.Length, MCU_IP_Addr, My_Port);
        }
    }

    // UNITY_RX_REQUEST_INT - Request the firmware perform UNITY_TX_UPDATE_INT
    private void request_int()
    {
        byte[] payload = new byte[1];   // packet is just one byte (command byte)
        payload[0] = 0x07;              // UNITY_RX_REQUEST_INT

        UDP_Socket.Send(payload, payload.Length, MCU_IP_Addr, My_Port);
    }


    /*********************************************************************************************************
    FIRMWARE FUNCTIONS OPERATIONS AND RELATED CODE

    **********************************************************************************************************/

    private string[] firmware_function_name;
    private int firmware_function_occupancy;      // how many firmware functions have been setup ?

    private void firmware_function_init()
    {
        firmware_function_name = new string[10];    // maximum 10 remote functions
        firmware_function_occupancy = 0;     // note : zeroing this value amounts to clearing firmware function storage
    }

    // UNITY_TX_SETUP_FUNCTION - register a firmware function so that it can called from the GUI
    private void parse_setup_function(byte[] data)
    {
        // note : the MCU transmits the index, but in reality this is redundant

        // extract the variable-length part at the end of a packet 
        byte[] str = new byte[data.Length - 2];     // function's "display name" starts at data[2]
        int i = 2;
        while (i < data.Length)
        {
            str[i - 2] = data[i];
            i++;
        }

        // store it as the variable's display name
        firmware_function_name[firmware_function_occupancy] = Encoding.UTF8.GetString(str);   // convert the variable length part into a string

        // log
        print("setup firmware function " + firmware_function_occupancy +  " : \"" + firmware_function_name[firmware_function_occupancy] + "\"");

        // update "occupancy" (max. index)
        firmware_function_occupancy++;
    }

    // UNITY_RX_CALL_FUNCTION - request a call to a firmware function
    private void firmware_function_call(int index)
    {
        // Create the operation's packet
        byte[] payload = new byte[2];   // packet is 2 bytes long : command byte, firmware function's index byte
        payload[0] = 0x03;              // UNITY_RX_CALL_FUNCTION
        payload[1] = (byte)index;
        UDP_Socket.Send(payload, payload.Length, MCU_IP_Addr, My_Port);
    }

}

