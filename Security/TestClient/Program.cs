using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace TestClient
{
    class Program
    {
        static void Main(string[] args)
        {
            // Data buffer for incoming data.
            byte[] bytes = new byte[1024];

            // Connect to a remote device.
            try {
                // Establish the remote endpoint for the socket.
                // This example uses port 11000 on the local computer.
                IPHostEntry ipHostInfo = Dns.Resolve(Dns.GetHostName());
                IPAddress ipAddress = ipHostInfo.AddressList[0];
                IPEndPoint remoteEP = new IPEndPoint(ipAddress,11000);

                // Create a TCP/IP  socket.
                Socket receiver = new Socket(AddressFamily.InterNetwork, 
                    SocketType.Stream, ProtocolType.Tcp );

                // Connect the socket to the remote endpoint. Catch any errors.
                try {
                    receiver.Connect(remoteEP);

                    Console.WriteLine("Socket connected to {0}",
                        receiver.RemoteEndPoint.ToString());

                    // Receive the response from the remote device.
                    for (int i = 0; i < 100; i++)
                    {
                        int bytesRec = receiver.Receive(bytes);
                        Console.WriteLine("Echoed test = {0}", bytesRec);
                    }

                    // Release the socket.
                    receiver.Shutdown(SocketShutdown.Both);
                    receiver.Close();
                
                } catch (ArgumentNullException ane) {
                    Console.WriteLine("ArgumentNullException : {0}",ane.ToString());
                } catch (SocketException se) {
                    Console.WriteLine("SocketException : {0}",se.ToString());
                } catch (Exception e) {
                    Console.WriteLine("Unexpected exception : {0}", e.ToString());
                }

            } catch (Exception e) {
                Console.WriteLine( e.ToString());
            }

            Console.ReadLine();
        }
    }
}
