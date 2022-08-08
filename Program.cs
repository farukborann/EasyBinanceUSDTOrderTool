using Binance.Net.Clients;
using Binance.Net.Enums;
using Binance.Net.Objects;
using EasyBinanceUSDTOrderTool;
using System;

// See https://aka.ms/new-console-template for more information

#nullable enable
string ilkMesaj = "Binance Kolay İşlem Açma Aracına Hoşgeldiniz.\n\n1) Api ayarlarını yapılandırma.\n2) Kaldıraç oranı ayarlama\n\nİşlem açmak için coinin adını yazıp boşluk bırakarak long işlem için 'l', short işlem için 's' yazın.\nOpsiyonel olarak close emri açtırmak için yüze parametresi girebilirsiniz.\nÖrn : btc s 5 (bitcoin, short, close %5 down)\n\nHesap bilgileri alınıyor lütfen bekleyin. ";
Console.Write(ilkMesaj);
BinanceClient.SetDefaultOptions(new BinanceClientOptions()
{
    UsdFuturesApiOptions = new BinanceApiClientOptions()
    {
        TradeRulesBehaviour = TradeRulesBehaviour.AutoComply
    }
});
User user = new User();
AppDomain.CurrentDomain.ProcessExit += (EventHandler)((s, ev) => user.CloseUser());
while (true)
{
    string _;
    do
    {
        Console.Write("\n-> ");
        _ = Console.ReadLine();
    }
    while (_ == null);
    string[] _params = _.Trim().Split(' ');
    if (_params.Length > 3)
        Console.WriteLine("Fazladan parametre tespit edildi! Lütfen kontrol edin.");
    else if (_params.Length == 1 && _params[0] == "1")
        user.SetApi();
    else if (_params.Length == 1 && _params[0] == "2")
    {
        int num1 = await user.SetLeverage() ? 1 : 0;
    }
    else if (_params.Length > 1)
    {
        string symbol = _params[0].ToLower() + "usdt";
        decimal percentClose = 0M;
        if (_params.Length == 3 && !decimal.TryParse(_params[2], out percentClose))
        {
            Console.WriteLine("Hatalı parametre girişi! Lütfen kontrol edin.");
        }
        else
        {
            PositionSide positionSide = _params[1] == "l" ? PositionSide.Long : PositionSide.Short;
            if (_params.Length == 2)
            {
                int num2 = await user.PlaceOrder(symbol, positionSide) ? 1 : 0;
            }
            else if (_params.Length == 3)
            {
                int num3 = await user.PlaceOrder(symbol, positionSide, new decimal?(percentClose)) ? 1 : 0;
            }
            symbol = (string)null;
            _ = (string)null;
            _params = (string[])null;
        }
    }
    else
        Console.WriteLine("Hatalı parametre girişi! Lütfen kontrol edin.");
}
