/*
 * Author: Ruy Delgado <ruydelgado@gmail.com>
 * Title: OpenDNS
 * Description: DNS Client Library
 * Revision: 1.0
 * Last Modified: 2005.01.28
 * Created On: 2005.01.28
 *
 * Note: Based on DnsLite by Jaimon Mathew
 * */

using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
//using System.Management;

namespace OpenDNS;

/// <summary>
/// DnsQuery Class
/// Handles the dns message transport and interpretation of result.
/// Use Send() to activate Result object.
/// </summary>
public class DnsQuery
{
    //consider killing
    private byte[] data;
    private int position, length;

    //Question
    public string Domain;
    public Types QueryType;
    public Classes QueryClass;
    public int Port;
    public ArrayList Servers;
    public bool RecursionDesired;

    //Internal Read-Only
    private DnsResponse _Response;

    //Public Properties
    public DnsResponse Response
    {
        get => _Response;
    }

    /// <summary>
    /// Default Constructor with QueryType: A
    /// </summary>
    public DnsQuery()
    {
        Port = 53;
        Servers = new ArrayList();
        QueryType = Types.A;
        QueryClass = Classes.IN;
    }

    public DnsQuery(string _Domain, Types _Type)
    {
        Port = 53;
        Servers = new ArrayList();
        QueryType = _Type;
        Domain = _Domain;
        QueryClass = Classes.IN;
        RecursionDesired = true;
    }

    /// <summary>
    /// Transmit message to each DNS servers
    /// until one returns a response object.
    /// </summary>
    /// <returns>True if response object is ready</returns>
    public bool Send()
    {

        CheckForServers();

        foreach (IPEndPoint Server in Servers)
        {
            var port = Server.Port;
            try
            {
                SendQuery2(Server.Address, port);
                break;
            }
            catch
            {
            }
        }

        return Response != null;
    }

    /// <summary>
    /// Uses UDPClient to send byte array to
    /// DNS Server Specified
    /// </summary>
    /// <param name="IPAddress">Target DNS Server</param>
    private void SendQuery(string ipAddress)
    {
        if (ipAddress == null)
            throw new ArgumentNullException();

        //opening the UDP socket at DNS server
        var dnsClient = new UdpClient(ipAddress, Port);

        //preparing the DNS query packet.
        var QueryPacket = MakeQuery();

        //send the data packet
        dnsClient.Send(QueryPacket, QueryPacket.Length);

        IPEndPoint endpoint = null;
        //receive the data packet from DNS server
        data = dnsClient.Receive(ref endpoint);

        length = data.Length;

        //un pack the byte array & makes an array of resource record objects.
        ReadResponse();

        //kill dns
        dnsClient.Close();
    }

    private void SendQuery2(IPAddress ipAddress, int port)
    {
        var timeout = 5000;

        if (ipAddress == null)
            throw new ArgumentNullException();

        //preparing the DNS query packet.
        var QueryPacket = MakeQuery();

        //opening the UDP socket at DNS server
        var serverAddress = ipAddress;
        EndPoint endPoint = new IPEndPoint(serverAddress, port);
        var socket = new Socket(serverAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendTimeout, timeout);
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, timeout);
        socket.SendTo(QueryPacket, endPoint);
        data = new byte[512];

        length = socket.ReceiveFrom(data, ref endPoint);

        //un pack the byte array & makes an array of resource record objects.
        ReadResponse();

        socket.Shutdown(SocketShutdown.Both);

    }

    /// <summary>
    /// Packs question into byte array format
    /// accepted by DNS servers
    /// </summary>
    /// <returns>Byte Array to Send</returns>
    private byte[] MakeQuery()
    {
        //Get New ID
        var QueryID = new Random().Next(55555);

        //Initialize Packet Byte Array
        var Question = new byte[512];
        for (var i = 0; i < 512; i++)
            Question[i] = 0;

        ////Fill Packet Header

        //SetID
        Question[0] = (byte)(QueryID >> 8);
        Question[1] = (byte)(QueryID & byte.MaxValue);
        Question[2] = (byte)1; //Set OpCode to Regular Query
        //Set bool bit for recursion desired
        Question[2] = (byte)(RecursionDesired ? Question[2] | 1 : Question[2] & 254);
        //Set Recursion Available (Filler)
        Question[3] = (byte)0;
        //Set Question Count
        Question[4] = (byte)0;
        Question[5] = (byte)1;

        ///Fill Question Section

        //Set Domain Name to Query
        var tokens = Domain.Split('.');
        string label;

        var Cursor = 12;

        for (var j = 0; j < tokens.Length; j++)
        {
            //Get domain segment
            label = tokens[j];
            //Set Length label for domain segment
            Question[Cursor++] = (byte)(label.Length & byte.MaxValue);
            //Get byte array of segment
            var b = Encoding.ASCII.GetBytes(label);
            //Transcribe array into packet
            for (var k = 0; k < b.Length; k++)
            {
                Question[Cursor++] = b[k];
            }
        }
        //End Domain Marker
        Question[Cursor++] = (byte)0;
        //Set Query type
        Question[Cursor++] = (byte)0;
        Question[Cursor++] = (byte)QueryType;
        //Set Query class
        Question[Cursor++] = (byte)0;
        Question[Cursor++] = (byte)QueryClass;

        return Question;
    }

    //for un packing the byte array
    private void ReadResponse()
    {
        /////////////////
        //HEADER
        var ID = ((data[0] & byte.MaxValue) << 8) + (data[1] & byte.MaxValue);
        var IS = (data[2] & 128) == 128;
        var OpCode = data[2] >> 3 & 15;
        var AA = (data[2] & 4) == 4;
        var TC = (data[2] & 2) == 2;
        var RD = (data[2] & 1) == 1;
        var RA = (data[3] & 128) == 128;
        var Z = data[3] & 1;//reserved, not used
        var RC = data[3] & 15;

        //Counts
        var QuestionCount = (data[4] & byte.MaxValue) << 8 | data[5] & byte.MaxValue;
        var AnswerCount = (data[6] & byte.MaxValue) << 8 | data[7] & byte.MaxValue;
        //Trace.WriteLine("Answer count: " + AnswerCount);
        var AuthorityCount = (data[8] & byte.MaxValue) << 8 | data[9] & byte.MaxValue;
        var AdditionalCount = (data[10] & byte.MaxValue) << 8 | data[11] & byte.MaxValue;

        //Create Response Object
        _Response = new DnsResponse(ID, AA, TC, RD, RA, RC);

        //FINISHED HEADER

        //GET QUESTIONS
        position = 12;

        for (var i = 0; i < QuestionCount; ++i)
        {

            var QuestionName = GetName();

            //two octec field
            var TypeID = (data[position++] & byte.MaxValue) << 8 | data[position++] & byte.MaxValue;
            var QuestionType = (Types)TypeID;

            //two octec field
            var ClassID = (data[position++] & byte.MaxValue) << 8 | data[position++] & byte.MaxValue;
            var QuestionClass = (Classes)ClassID;
        }

        for (var i = 0; i < AnswerCount; ++i)
            GetResourceRecord(i, _Response.Answers);

        for (var i = 0; i < AuthorityCount; ++i)
            GetResourceRecord(i, _Response.Authorities);

        for (var i = 0; i < AdditionalCount; ++i)
            GetResourceRecord(i, _Response.AdditionalRecords);

    }

    private void GetResourceRecord(int i, ResourceRecordCollection Container)
    {
        //get resource (answer) name
        var ResourceName = GetName();

        //get resource type and class, usefull when using the ANY query
        //type: two octec field
        var TypeID = (data[position++] & byte.MaxValue) << 8 | data[position++] & byte.MaxValue;
        var ResourceType = (Types)TypeID;

        //type: two octec field
        var ClassID = (data[position++] & byte.MaxValue) << 8 | data[position++] & byte.MaxValue;
        var ResourceClass = (Classes)ClassID;

        //ttl: unsigned integer
        var TTL_Seconds = (data[position++] & byte.MaxValue) << 24 | (data[position++] & byte.MaxValue) << 16 | (data[position++] & byte.MaxValue) << 8 | data[position++] & byte.MaxValue;

        //Get Resource Data Length
        var RDLength = (data[position++] & byte.MaxValue) << 8 | data[position++] & byte.MaxValue;

        //Parse Resource Data: 4 possible formats: A, Text, SOA and MX
        switch (ResourceType)
        {
            case Types.A:
                //Get IP Address Blocks
            {
                var bs = new[] { (byte)(data[position++] & byte.MaxValue), (byte)(data[position++] & byte.MaxValue), (byte)(data[position++] & byte.MaxValue), (byte)(data[position++] & byte.MaxValue) };
                var ResourceAddress = string.Concat(bs[0], ".", bs[1], ".", bs[2], ".", bs[3]);

                var rrA = new Address(ResourceName, ResourceType, ResourceClass, TTL_Seconds, ResourceAddress);
                Container.Add(rrA);
                break;
            }
            case Types.AAAA:
            {
                //Get IP Address Blocks
                var bs = new ushort[8];
                for (var j = 0; j < 8; ++j) bs[j] = (ushort)((byte)(data[position + j * 2] & byte.MaxValue) << 8 | (byte)(data[position + j * 2 + 1] & byte.MaxValue));
                position += 16;
                var ResourceAddress = string.Concat(new object[] {
                    Convert.ToString(bs[0], 16), ":",
                    Convert.ToString(bs[1], 16), ":",
                    Convert.ToString(bs[2], 16), ":",
                    Convert.ToString(bs[3], 16), ":",
                    Convert.ToString(bs[4], 16), ":",
                    Convert.ToString(bs[5], 16), ":",
                    Convert.ToString(bs[6], 16), ":",
                    Convert.ToString(bs[7], 16)});

                var rrA = new Address(ResourceName, ResourceType, ResourceClass, TTL_Seconds, ResourceAddress);
                Container.Add(rrA);
                break;
            }
            case Types.SOA:
            {
                //Extract Text Fields
                var Server = GetName();
                var Email = GetName();

                //32 bit fields
                long Serial = (data[position++] & byte.MaxValue) << 24 | (data[position++] & byte.MaxValue) << 16 | (data[position++] & byte.MaxValue) << 8 | data[position++] & byte.MaxValue;
                long Refresh = (data[position++] & byte.MaxValue) << 24 | (data[position++] & byte.MaxValue) << 16 | (data[position++] & byte.MaxValue) << 8 | data[position++] & byte.MaxValue;
                long Retry = (data[position++] & byte.MaxValue) << 24 | (data[position++] & byte.MaxValue) << 16 | (data[position++] & byte.MaxValue) << 8 | data[position++] & byte.MaxValue;
                long Expire = (data[position++] & byte.MaxValue) << 24 | (data[position++] & byte.MaxValue) << 16 | (data[position++] & byte.MaxValue) << 8 | data[position++] & byte.MaxValue;
                long Minimum = (data[position++] & byte.MaxValue) << 24 | (data[position++] & byte.MaxValue) << 16 | (data[position++] & byte.MaxValue) << 8 | data[position++] & byte.MaxValue;

                var rrSOA = new SOA(ResourceName, ResourceType, ResourceClass, TTL_Seconds, Server, Email, Serial, Refresh, Retry, Expire, Minimum);
                Container.Add(rrSOA);

                break;
            }
            case Types.CNAME:
            case Types.MINFO:
            case Types.NS:
            case Types.PTR:
            case Types.TXT:
                //Simplest RDATA format, just a text string, shared by many
                var ResourceDataText = GetName();
                var rrTXT = new ResourceRecord(ResourceName, ResourceType, ResourceClass, TTL_Seconds, ResourceDataText);
                Container.Add(rrTXT);
                break;

            case Types.MX:
                var Rank = data[position++] << 8 | data[position++] & byte.MaxValue;
                var Exchange = GetName();

                var rrMX = new MX(ResourceName, ResourceType, ResourceClass, TTL_Seconds, Rank, Exchange);
                Container.Add(rrMX);

                break;
            default:
                Trace.WriteLine($"Resource type did not match: {ResourceType}", "RUY QDNS");
                break;
        }
    }

    /// <summary>
    /// Wrapper for not so pretty code =)
    /// </summary>
    /// <returns></returns>
    private string GetName()
    {
        var sb = new StringBuilder();
        position = ExtractName(position, sb);
        return sb.ToString();
    }

    /// <summary>
    /// Gets name string segments from byte array.
    /// Uses the DNS "compression" support
    /// that gives a pointer to a previous
    /// occurrence of repeat names.
    /// -- not so pretty, consider killing
    /// </summary>
    /// <param name="position">Current Byte Array Reading Position</param>
    /// <returns>New Global Cursor Position</returns>
    private int ExtractName(int ResourceDataCursor, StringBuilder Name)
    {
        //Get label for how many characters to extract in this segment
        var LengthLabel = data[ResourceDataCursor++] & byte.MaxValue;

        if (LengthLabel == 0)
        {
            return ResourceDataCursor;
        }

        do
        {
            if ((LengthLabel & 0xC0) == 0xC0)
            {
                if (ResourceDataCursor >= length)
                {
                    return -1;
                }

                //Compression OffsetID for RDATA Compression
                var CompressionOffsetID = (LengthLabel & 0x3F) << 8 | data[ResourceDataCursor++] & byte.MaxValue;
                ExtractName(CompressionOffsetID, Name);
                return ResourceDataCursor;
            }
            if (ResourceDataCursor + LengthLabel > length)
            {
                return -1;
            }

            Name.Append(Encoding.ASCII.GetString(data, ResourceDataCursor, LengthLabel));
            ResourceDataCursor += LengthLabel;

            if (ResourceDataCursor > length)
            {
                return -1;
            }

            LengthLabel = data[ResourceDataCursor++] & byte.MaxValue;

            //if new length label is larger than 0, we have another segment
            //so append dot.
            if (LengthLabel != 0)
            {
                Name.Append('.');
            }
        }
        while (LengthLabel != 0);

        return ResourceDataCursor;
    }

    /// <summary>
    /// Checks for any DNS servers
    /// on the public collection. If user
    /// did not add any manually gets
    /// the default ones from the TCP/IP
    /// Configuration.
    /// </summary>
    /// <returns>True if we have at least one DNS server</returns>
    private bool CheckForServers()
    {
        //Check if user added servers
        if (Servers.Count == 0)
            Servers = GetDefaultServers();

        if (Servers.Count > 0)
        {
            return true;
        }
        throw new Exception("Abort: No DNS servers specified manually and could not get default ones.");
        //return false;
    }

    /// <summary>
    /// TODO:
    /// Gets DNS Servers from TCP/IP Configuration of
    /// network adapter.
    /// </summary>
    /// <returns>ArrayList object. May be empty if no servers are found.</returns>
    private ArrayList GetDefaultServers()
    {

        var LocalServers = new ArrayList();

        try
        {
            //Insert code here to query network adapter.

        }
        catch (Exception Ex)
        {
            Trace.WriteLine($"Could not get DNS servers from network adapter: {Ex.Message}", "OpenDNS");
        }
        finally
        {

        }

        return LocalServers;
    }

}