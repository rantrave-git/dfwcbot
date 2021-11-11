
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DfwcResultsBot
{
    public class ChatMessage
    {
        public string Sender { get; set; }
        public string Message { get; set; }
    }
    public interface IChatApi
    {
        Task SendMessage(string message);
        IAsyncEnumerable<ChatMessage> RecvMessages();
    }
    interface IConnection
    {
        Task Connect(string nickname, string auth);
        Task Disconnect();
        void Dispose();
        Task<IChatApi> JoinChannel(string channel);
        ValueTask LeaveChannel(string channel);
        void Terminate();
    }
}