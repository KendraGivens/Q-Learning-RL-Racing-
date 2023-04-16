using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net;
using System;
using System.Linq;
using UnityEngine;

public class Model 
{
    private UdpClient client;
    IPEndPoint ep;
    IPEndPoint rep;

    public Model()
    {
        client = new UdpClient();
        ep = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 1234);
        rep = new IPEndPoint(IPAddress.Any, 0);
    }

    public void Send(byte[] bytes)
    {
        client.Send(bytes, bytes.Length, ep);
    }

    public byte[] Receive()
    {
        byte[] receivedData = client.Receive(ref rep);

        return receivedData;
    }

    //In: State space
    //Out: Nothing
    public void Fit(List<float> previous_state, float q_value)
    {
        //Convert state to bytes   
        byte prepend_byte = 1; //Byte is 1 for training    
        Send(ConvertToBytes(previous_state, prepend_byte, q_value, true)); //Send current state to server
    }

    public byte[] ConvertToBytes(List<float> state, byte prepend_byte, List<float> q_values, bool Q)
    {
        //Convert state to bytes 
        byte[] byte_state = state.SelectMany(BitConverter.GetBytes).ToArray();
        byte[] byte_packet = new byte[byte_state.Length + 1];
        byte_packet[0] = prepend_byte;
        Buffer.BlockCopy(byte_state, 0, byte_packet, 1, byte_state.Length);

        //Add q value to the byte array for training
       /* if (Q)
        {
            byte[] q_bytes = BitConverter.GetBytes(q_value);
            byte[] byte_packet_q = new byte[byte_packet.Length + 1];
            byte_packet_q = new byte[byte_packet.Length + 1];
            byte_packet_q[0] = byte_packet_q;
            Buffer.BlockCopy(byte_packet, 0, byte_packet_q, 1, byte_packet.Length);
            return byte_packet_q;
        }
       */
        return byte_packet;
    }

    public List<float> ConvertToFloats(byte[] byte_prediction)
    {
        //Convert byte prediction to list of floats 
        float[] floats = new float[byte_prediction.Length / 4];
        Buffer.BlockCopy(byte_prediction, 0, floats, 0, byte_prediction.Length);
        List<float> float_prediction = floats.ToList();
        
        return float_prediction;
    }

    //In: State space
    //Out: Actions (Expected reward)
    public List<float> Predict(List<float> state)
    {
        //Convert state to bytes  
        byte prepend_byte = 0; //Byte is 0 for prediction     
        Send(ConvertToBytes(state, prepend_byte, 0.0f, false)); //Send current state to server

        //Receive prediction from model
        byte[] byte_prediction = Receive();
        
        //Pass prediction to agent
        return ConvertToFloats(byte_prediction);
    }
}
