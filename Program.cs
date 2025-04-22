using DotNetEnv;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace RegiBot
{
    class Program
    {
        private static CancellationTokenSource _cts;
        private static TelegramBotClient _botClient;
        static async Task Main(string[] args)
        {
            Env.Load();
            _cts = new CancellationTokenSource();
            _botClient = new TelegramBotClient(Env.GetString("BOT_TOKEN"), cancellationToken: _cts.Token);

            await _botClient.DeleteWebhook();
            await _botClient.DropPendingUpdates();

            _botClient.OnMessage += MessageHandler;

            var me = await _botClient.GetMe();

            Console.WriteLine($"Bot {me.Username} is started... Press escape to terminate");

            while(Console.ReadKey(true).Key != ConsoleKey.Escape) { }

            _cts.Cancel();
        }

        private static async Task MessageHandler(Message message, UpdateType type)
        {

        }
    }
}
