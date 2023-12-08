using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR;

namespace CoinCapParserService
{
    public class MyHub : Hub
    {
        private string _filePathSignalR = @"c:\coin365SignalR.txt";

        public void AddMessage(string name, string message)
        {
            Console.WriteLine("Hub AddMessage {0} {1}\n", name, message);
            Clients.All.addMessage(name, message);
        }

        public void Heartbeat()
        {
            _LogSignalRMessage("Heart beat");
            Clients.All.heartbeat();
        }

        public void SendCurrencyObject(List<CurrencyModelForCache> lstCurrencyModel)
        {
            _LogSignalRMessage("Hub currency " + lstCurrencyModel[0].base_currency);
            Clients.All.sendHelloObject(lstCurrencyModel);
        }

        public override Task OnConnected()
        {
            _LogSignalRMessage("Hub OnConnected " + Context.ConnectionId);
            return (base.OnConnected());
        }

        public override Task OnDisconnected(bool stopCalled)
        {
            if (stopCalled)
            {
                _LogSignalRMessage("Stop called true" + Context.ConnectionId);
            }
            else
            {
                _LogSignalRMessage("Stop called false" + Context.ConnectionId);
            }
            return base.OnDisconnected(stopCalled);
        }

        public override Task OnReconnected()
        {
            _LogSignalRMessage("Hub OnReconnected " + Context.ConnectionId);
            return (base.OnDisconnected(true));
        }

        private void _LogSignalRMessage(string message)
        {
            using (StreamWriter _streamWriter = File.AppendText(_filePathSignalR))
            {
                _streamWriter.WriteLine(message);
                _streamWriter.WriteLine("=======================================================================");
            }
        }
    }
}