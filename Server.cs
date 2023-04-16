using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Tensorflow;
using Keras.Layers;
using Keras.Models;
using Keras.Optimizers;
using Python.Runtime;
using Numpy;
using System;
using System.Net.Sockets;

namespace ConsoleApp7
{
    internal class Server
    {
        static void Main(string[] args)
        {
            using (Py.GIL())
            {
                List<int> operation = new List<int> { 0, 1 };   
                byte[] operation_byte = operation.SelectMany(BitConverter.GetBytes).ToArray();

                //NDarray x = np.array(new float[,] { { 0, 0 }, { 0, 1 }, { 1, 0 }, { 1, 1 } });
                //NDarray y = np.array(new float[] { 0, 1, 1, 0 });

                //Build functional model
                var state = new Input(shape: new Keras.Shape(10));
                var h1 = new Dense(64, activation: "relu").Set(state);
                var actions = new Dense(10, activation: "sigmoid").Set(h1);              

                var model = new Keras.Models.Model(new Input[] { state }, new BaseLayer[] { actions });
                model.Summary();
                model.Compile(optimizer: "Adam", loss: "mse", metrics: new string[] { "accuracy" });


                UdpClient udpServer = new UdpClient(1234);
                Console.WriteLine("UDP server started on port 1234");

                while (true)
                {
                    IPEndPoint remoteEP = null;
                    byte[] receivedData = udpServer.Receive(ref remoteEP);

                    Console.WriteLine("Received data from {0}:\n{1}", remoteEP, receivedData);

                    byte[] response = new byte[receivedData.Length - 1];
                    System.Buffer.BlockCopy(receivedData, 1, response, 0, response.Length);
                    udpServer.Send(response, response.Length, remoteEP);

                    if (response[0] == operation_byte[0])
                    {
                       // model.Fit(x, y, batch_size: 1, epochs: 1, verbose: 1);
                    }
                    if (response[0] == operation_byte[1])
                    {
                        // model.Predict();
                    }


                }
            }
        }
    }
}