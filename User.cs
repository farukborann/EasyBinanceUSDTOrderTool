using Binance.Net;
using Binance.Net.Clients;
using Binance.Net.Enums;
using Binance.Net.Interfaces;
using Binance.Net.Interfaces.Clients.UsdFuturesApi;
using Binance.Net.Objects;
using Binance.Net.Objects.Models;
using Binance.Net.Objects.Models.Futures;
using Binance.Net.Objects.Models.Futures.Socket;
using Binance.Net.Objects.Models.Spot;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Sockets;

namespace EasyBinanceUSDTOrderTool
{
    public class User
    {
        private string? ApiKey;
        private string? ApiSecret;
        private string? ListenKey;
        private BinanceClient Client;
        private BinanceSocketClient SocketClient;
        private Dictionary<string, int> Leverages;
        private Dictionary<string, decimal> Prices;
        private decimal UsdtBalance;
        private int PercentUsdtBalance = 99;

        public void SetApi()
        {
            Console.Write("-> Api Key : ");
            this.ApiKey = Console.ReadLine();
            Console.Write("-> Api Secret : ");
            this.ApiSecret = Console.ReadLine();
            Console.Write("-> Bakiye Yüzdesi : ");
            int.TryParse(Console.ReadLine(), out PercentUsdtBalance);
            if (string.IsNullOrEmpty(this.ApiKey) || string.IsNullOrEmpty(this.ApiSecret) || PercentUsdtBalance < 1)
            {
                Console.WriteLine("Api ayarları boş geçilemez. Bakiye yüzdesi 1 den düşük olamaz. Lütfen tekrar ayarlayın.\n");
                this.SetApi();
            }
            else
            {
                using (StreamWriter streamWriter = new StreamWriter("conf.config"))
                {
                    streamWriter.WriteLine(this.ApiKey);
                    streamWriter.WriteLine(this.ApiSecret);
                    streamWriter.WriteLine(this.PercentUsdtBalance);
                }
            }
        }

        private void GetApi()
        {
            while (string.IsNullOrEmpty(this.ApiKey) || string.IsNullOrEmpty(this.ApiSecret))
            {
                if (!File.Exists("conf.config"))
                {
                    Console.WriteLine("\nApi ayar dosyası bulunamadı! Lütfen api ayarlarınızı yapılandırın.");
                    this.SetApi();
                }
                else
                {
                    string[] strArray = File.ReadAllLines("conf.config");
                    if (strArray.Length != 3)
                    {
                        Console.WriteLine("\nApi ayar dosyası bozuk! Lütfen api ayarlarınızı baştan yapılandırın.");
                        this.SetApi();
                    }
                    else
                    {
                        this.ApiKey = strArray[0];
                        this.ApiSecret = strArray[1];
                        _ = int.TryParse(strArray[2], out PercentUsdtBalance);
                    }
                }
            }
        }

        public async Task<decimal> CalcMaxQuantity(
          string Symbol,
          decimal Quantity,
          int Leverage,
          decimal Price,
          decimal UsdtBalance,
          PositionSide PositionSide)
        {
            WebCallResult<BinanceFuturesMarkPrice> __ = await this.Client.UsdFuturesApi.ExchangeData.GetMarkPriceAsync(Symbol);
            if (!__.Success)
            {
                Console.WriteLine("Mark fiyat bilgisi alınamadı! Hata : " + (__.Error == null ? "" : __.Error.Message));
                return 0M;
            }
            decimal markPrice = __.Data.MarkPrice;
            decimal notional = Quantity * markPrice;
            WebCallResult<IEnumerable<BinancePositionDetailsUsdt>> ___ = await this.Client.UsdFuturesApi.Account.GetPositionInformationAsync(Symbol);
            if (!___.Success)
            {
                Console.WriteLine("Mark fiyat bilgisi alınamadı! Hata : " + (___.Error == null ? "" : ___.Error.Message));
                return 0M;
            }
            BinancePositionDetailsUsdt positionInfo = ___.Data.FirstOrDefault<BinancePositionDetailsUsdt>((Func<BinancePositionDetailsUsdt, bool>)(x => string.Equals(x.Symbol.ToLower(), Symbol.ToLower(), StringComparison.CurrentCultureIgnoreCase)));
            WebCallResult<BinanceBookPrice> bookPrice = await this.Client.UsdFuturesApi.ExchangeData.GetBookPriceAsync(Symbol);
            if (!bookPrice.Success)
            {
                Console.WriteLine("Kitap fiyatı bilgisi alınamadı! Hata : " + (bookPrice.Error == null ? "" : bookPrice.Error.Message));
                return 0M;
            }
            decimal Max = 0M;
            if (PositionSide == PositionSide.Long)
            {
                decimal up = Leverage * UsdtBalance;
                decimal down = bookPrice.Data.BestAskPrice * 1.0005M + Math.Max(Leverage * (bookPrice.Data.BestAskPrice * 1.0005M - markPrice), 0M);
                decimal left = up / down;
                decimal right = (positionInfo.MaxNotional - notional) / Price;
                Max = Math.Min(left, right) * Price;
                Console.WriteLine($"Max Price Hesaplandı => {Math.Round(Max, 5)}");
            }
            else
            {
                decimal up = Leverage * UsdtBalance;
                decimal down = Math.Max(bookPrice.Data.BestBidPrice, markPrice);
                decimal left = up / down;
                decimal right = (positionInfo.MaxNotional - notional) / Price;
                Max = Math.Min(left, right) * Price;
                Console.WriteLine($"Max Price Hesaplandı => {Math.Round(Max, 5)}");
            }
            return Max;
        }

        public async Task<bool> SetLeverage()
        {
            Console.Write("-> Coin Adı (Örn : btc) : ");
            string symbol = Console.ReadLine().ToLower() + "usdt";
            Console.Write("->Kaldıraç (Örn : 20) : ");
            string _ = Console.ReadLine();
            int leverage;
            if (!int.TryParse(_, out leverage))
            {
                Console.WriteLine("-> Hatalı parametre girildi! Lütfen tekrar deneyin.\n");
                this.SetLeverage();
                return false;
            }
            WebCallResult<BinanceFuturesInitialLeverageChangeResult> changeLeverage = await this.Client.UsdFuturesApi.Account.ChangeInitialLeverageAsync(symbol, leverage);
            if (!changeLeverage.Success)
            {
                Console.WriteLine("Kaldıraç oranı ayarlanamadı! Hata : " + (changeLeverage.Error == null ? "" : changeLeverage.Error.Message) + "\nLütfen tekrar deneyin.");
                return false;
            }
            Console.WriteLine("Kaldıraç oranı ayarlandı.");
            return true;
        }

        public async Task<bool> PlaceOrder(
          string Symbol,
          PositionSide PositionSide,
          decimal? PercentClose = null)
        {
            OrderSide orderSide = PositionSide == PositionSide.Long ? OrderSide.Buy : OrderSide.Sell;
            int num1;
            if (Leverages.TryGetValue(Symbol, out num1))
            {
                int Leverage = Leverages[Symbol];
                if (Prices.TryGetValue(Symbol, out decimal _))
                {
                    decimal Price = Prices[Symbol];
                    decimal Quantity = UsdtBalance * PercentUsdtBalance / 100 / Price * Leverage;
                    IBinanceClientUsdFuturesApiTrading trading = this.Client.UsdFuturesApi.Trading;
                    string symbol = Symbol;
                    int side = (int)orderSide;
                    decimal? quantity = new decimal?(Quantity);
                    WorkingType? nullable = new WorkingType?(WorkingType.Mark);
                    decimal? price = new decimal?();
                    PositionSide? positionSide = new PositionSide?();
                    TimeInForce? timeInForce = new TimeInForce?();
                    bool? reduceOnly = new bool?();
                    decimal? stopPrice = new decimal?();
                    decimal? activationPrice = new decimal?();
                    decimal? callbackRate = new decimal?();
                    WorkingType? workingType = nullable;
                    bool? closePosition = new bool?();
                    OrderResponseType? orderResponseType = new OrderResponseType?();
                    bool? priceProtect = new bool?();
                    int? receiveWindow = new int?();
                    CancellationToken ct = new CancellationToken();
                    WebCallResult<BinanceFuturesPlacedOrder> resultOrder = await trading.PlaceOrderAsync(symbol, (OrderSide)side, FuturesOrderType.Market, quantity, price, positionSide, timeInForce, reduceOnly, stopPrice: stopPrice, activationPrice: activationPrice, callbackRate: callbackRate, workingType: workingType, closePosition: closePosition, orderResponseType: orderResponseType, priceProtect: priceProtect, receiveWindow: receiveWindow, ct: ct);
                    if (resultOrder.Success)
                    {
                        Console.WriteLine("İşlem başarıyla açıldı.");
                    }
                    else
                    {
                        int num2;
                        if (resultOrder != null && resultOrder.Error != null)
                        {
                            int? code = resultOrder.Error.Code;
                            num1 = 4005;
                            num2 = !(code.GetValueOrDefault() == num1 & code.HasValue) ? 1 : 0;
                        }
                        else
                            num2 = 1;
                        if (num2 != 0)
                        {
                            Console.WriteLine("İşlem açılamadı! Hata : " + (resultOrder == null || resultOrder.Error == null ? "" : resultOrder.Error.Message));
                            return false;
                        }
                        Console.WriteLine("Maximum qauntity hatası. MaxQty ile işlem açma deneniyor.");
                        int num3 = await PlaceOrderWithMaxQuantity(Symbol, Quantity, Leverage, Price, this.UsdtBalance, orderSide, PositionSide, PercentClose) ? 1 : 0;
                    }
                    if (resultOrder.Success && PercentClose.HasValue)
                    {
                        int num4 = await PlaceCloseOrder(Symbol, Quantity, Price, orderSide, PositionSide, PercentClose) ? 1 : 0;
                    }
                    return true;
                }
                Console.WriteLine("Fiyat bilgisi bulunamadı!");
                return false;
            }
            Console.WriteLine("Kaldıraç bilgisi bulunamadı!");
            return false;
        }

        public async Task<bool> PlaceOrderWithMaxQuantity(string Symbol, decimal Quantity, int Leverage, decimal Price, decimal UsdtBalance, OrderSide orderSide, PositionSide PositionSide, decimal? PercentClose)
        {
            Quantity = CalcMaxQuantity(Symbol, Quantity, Leverage, Price, UsdtBalance, PositionSide).Result;

            var resultOrder = await Client.UsdFuturesApi.Trading.PlaceOrderAsync(Symbol, orderSide, FuturesOrderType.Market, Quantity);
            if (resultOrder.Success)
            {
                Console.WriteLine("İşlem başarıyla açıldı.");
                return true;
            }
            Console.WriteLine("İşlem açılamadı! Hata : " + (resultOrder == null || resultOrder.Error == null ? "" : resultOrder.Error.Message));
            return false;
        }

        public async Task<bool> PlaceCloseOrder(string Symbol, decimal Quantity, decimal Price, OrderSide orderSide, PositionSide PositionSide, decimal? PercentClose)
        {
            var exchangeInfo = await this.Client.UsdFuturesApi.ExchangeData.GetExchangeInfoAsync();
            if (!exchangeInfo.Success)
            {
                Console.WriteLine("Değişim bilgisi alınamadı! Hata : " + (exchangeInfo.Error == null ? "" : exchangeInfo.Error.Message));
                return false;
            }
            BinanceFuturesUsdtSymbol symbolData = exchangeInfo.Data.Symbols.SingleOrDefault<BinanceFuturesUsdtSymbol>((Func<BinanceFuturesUsdtSymbol, bool>)(x => string.Equals(x.Name.ToLower(), Symbol.ToLower(), StringComparison.CurrentCultureIgnoreCase)));
            OrderSide orderSideClose = orderSide == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
            decimal? nullable1;
            decimal? nullable2;
            if (PositionSide != PositionSide.Long)
            {
                decimal num1 = Price;
                decimal num2 = (decimal)100;
                decimal? nullable3 = PercentClose;
                decimal? nullable4 = nullable3.HasValue ? new decimal?(num2 - nullable3.GetValueOrDefault()) : new decimal?();
                decimal? nullable5;
                if (!nullable4.HasValue)
                {
                    nullable3 = new decimal?();
                    nullable5 = nullable3;
                }
                else
                    nullable5 = new decimal?(num1 * nullable4.GetValueOrDefault());
                nullable1 = nullable5;
                decimal num3 = (decimal)100;
                nullable2 = nullable1.HasValue ? new decimal?(nullable1.GetValueOrDefault() / num3) : new decimal?();
            }
            else
            {
                decimal num4 = Price;
                decimal num5 = (decimal)100;
                decimal? nullable6 = PercentClose;
                decimal? nullable7 = nullable6.HasValue ? new decimal?(num5 + nullable6.GetValueOrDefault()) : new decimal?();
                decimal? nullable8;
                if (!nullable7.HasValue)
                {
                    nullable6 = new decimal?();
                    nullable8 = nullable6;
                }
                else
                    nullable8 = new decimal?(num4 * nullable7.GetValueOrDefault());
                nullable1 = nullable8;
                decimal num6 = (decimal)100;
                nullable2 = nullable1.HasValue ? new decimal?(nullable1.GetValueOrDefault() / num6) : new decimal?();
            }
            decimal? stopPrice = nullable2;
            stopPrice = new decimal?(BinanceHelpers.ClampQuantity(symbolData.PriceFilter.MinPrice, symbolData.PriceFilter.MaxPrice, symbolData.PriceFilter.TickSize, stopPrice.Value));
            IBinanceClientUsdFuturesApiTrading trading = this.Client.UsdFuturesApi.Trading;
            string symbol = Symbol;
            int side = (int)orderSideClose;
            decimal? quantity = new decimal?(Quantity);
            nullable1 = stopPrice;
            WorkingType? nullable9 = new WorkingType?(WorkingType.Mark);
            decimal? price = new decimal?();
            PositionSide? positionSide = new PositionSide?();
            TimeInForce? timeInForce = new TimeInForce?();
            bool? reduceOnly = new bool?();
            decimal? stopPrice1 = nullable1;
            decimal? activationPrice = new decimal?();
            decimal? callbackRate = new decimal?();
            WorkingType? workingType = nullable9;
            bool? closePosition = new bool?();
            OrderResponseType? orderResponseType = new OrderResponseType?();
            bool? priceProtect = new bool?();
            int? receiveWindow = new int?();
            CancellationToken ct = new CancellationToken();
            WebCallResult<BinanceFuturesPlacedOrder> resultOrderClose = await trading.PlaceOrderAsync(symbol, (OrderSide)side, FuturesOrderType.TakeProfitMarket, quantity, price, positionSide, timeInForce, reduceOnly, stopPrice: stopPrice1, activationPrice: activationPrice, callbackRate: callbackRate, workingType: workingType, closePosition: closePosition, orderResponseType: orderResponseType, priceProtect: priceProtect, receiveWindow: receiveWindow, ct: ct);
            if (resultOrderClose.Success)
            {
                Console.WriteLine("Close açık emri başarıyla açıldı.");
                return true;
            }
            Console.WriteLine("Close açık emri açılamadı! Hata : " + (resultOrderClose.Error == null ? "" : resultOrderClose.Error.Message));
            return false;
        }

        public async Task CloseUser()
        {
            WebCallResult<object> webCallResult = await this.Client.UsdFuturesApi.Account.StopUserStreamAsync(this.ListenKey);
        }

        private async Task<bool> GetAccountInformation()
        {
            WebCallResult<IEnumerable<BinancePrice>> requestPrices = await this.Client.UsdFuturesApi.ExchangeData.GetPricesAsync();
            if (requestPrices.Success)
            {
                requestPrices.Data.ToList<BinancePrice>().ForEach((Action<BinancePrice>)(x => this.Prices.Add(x.Symbol.ToLower(), x.Price)));
                for (int i = 0; i < this.Prices.Count; ++i)
                {
                    WebCallResult<IEnumerable<BinancePositionDetailsUsdt>> request = await this.Client.UsdFuturesApi.Account.GetPositionInformationAsync(this.Prices.ElementAt<KeyValuePair<string, decimal>>(i).Key);
                    if (request.Success)
                    {
                        BinancePositionDetailsUsdt posData = request.Data.First<BinancePositionDetailsUsdt>();
                        this.Leverages.Add(posData.Symbol.ToLower(), posData.Leverage);
                        posData = (BinancePositionDetailsUsdt)null;
                    }
                    else
                        Console.WriteLine("Hesap Bilgisi Alınamadı. Lütfen düzgün bir internet bağlantınız olduğunu kontrol edin.\n Hata : " + request.Error.Message);
                    request = (WebCallResult<IEnumerable<BinancePositionDetailsUsdt>>)null;
                }
                return true;
            }
            Console.WriteLine("Binance veri akışında hata : " + (requestPrices.Error == null ? "NULL" : requestPrices.Error.Message));
            return false;
        }

        private async Task<bool> SubscribeAccountInfoUpdates()
        {
            WebCallResult<string> StartKey = await this.Client.UsdFuturesApi.Account.StartUserStreamAsync();
            if (!StartKey.Success)
            {
                Console.WriteLine("Hesap güncellemeline abone olunamadı. Lütfen Api ayarlarınızı yapıp programı yeniden başlatın.");
                return false;
            }
            CallResult<UpdateSubscription> subResult = await this.SocketClient.UsdFuturesStreams.SubscribeToUserDataUpdatesAsync(StartKey.Data, (Action<DataEvent<BinanceFuturesStreamConfigUpdate>>)(data =>
            {
                BinanceFuturesStreamLeverageUpdateData leverageUpdateData = data.Data.LeverageUpdateData;
                if (this.Leverages.ContainsKey(leverageUpdateData.Symbol.ToLower()))
                    this.Leverages[leverageUpdateData.Symbol.ToLower()] = leverageUpdateData.Leverage;
                else
                    this.Leverages.Add(leverageUpdateData.Symbol.ToLower(), leverageUpdateData.Leverage);
            }), (Action<DataEvent<BinanceFuturesStreamMarginUpdate>>)null, (Action<DataEvent<BinanceFuturesStreamAccountUpdate>>)(data =>
            {
                if (!data.Data.UpdateData.Balances.Any<BinanceFuturesStreamBalance>((Func<BinanceFuturesStreamBalance, bool>)(x => x.Asset == "USDT")))
                    return;
                this.UsdtBalance = data.Data.UpdateData.Balances.First<BinanceFuturesStreamBalance>((Func<BinanceFuturesStreamBalance, bool>)(x => x.Asset.Equals("USDT"))).WalletBalance;
            }), (Action<DataEvent<BinanceFuturesStreamOrderUpdate>>)null, (Action<DataEvent<BinanceStreamEvent>>)(data => this.SubscribeAccountInfoUpdates()));
            if (!subResult.Success)
            {
                Console.WriteLine("Hesap güncellemeline abone olunamadı. Lütfen Api ayarlarınızı yapıp programı yeniden başlatın.");
                return false;
            }
            this.ListenKey = StartKey.Data;
            return true;
        }

        private async Task<bool> SubscribePriceUpdates()
        {
            CallResult<UpdateSubscription> subscribeResult = await this.SocketClient.UsdFuturesStreams.SubscribeToAllTickerUpdatesAsync((Action<DataEvent<IEnumerable<IBinanceTick>>>)(data =>
            {
                foreach (IBinanceTick binanceTick in data.Data)
                {
                    if (this.Prices.TryGetValue(binanceTick.Symbol.ToLower(), out decimal _))
                        this.Prices[binanceTick.Symbol.ToLower()] = binanceTick.LastPrice;
                }
            }));
            if (subscribeResult.Success)
                return true;
            Console.WriteLine($"Binance fiyat güncellemelerine katılmada hata (Usd Futures) : {subscribeResult.Error}");
            return false;
        }

        public User()
        {
            this.GetApi();
            BinanceClientOptions options1 = new BinanceClientOptions();
            options1.ApiCredentials = new ApiCredentials(this.ApiKey, this.ApiSecret);
            options1.UsdFuturesApiOptions = new BinanceApiClientOptions()
            {
                TradeRulesBehaviour = TradeRulesBehaviour.AutoComply
            };
            this.Client = new BinanceClient(options1);
            BinanceSocketClientOptions options2 = new BinanceSocketClientOptions();
            options2.ApiCredentials = new ApiCredentials(this.ApiKey, this.ApiSecret);
            options2.UsdFuturesStreamsOptions = (ApiClientOptions)new BinanceApiClientOptions()
            {
                TradeRulesBehaviour = TradeRulesBehaviour.AutoComply
            };
            this.SocketClient = new BinanceSocketClient(options2);
            this.Leverages = new Dictionary<string, int>();
            this.Prices = new Dictionary<string, decimal>();
            this.GetAccountInformation().Wait();
            this.SubscribePriceUpdates().Wait();
            this.SubscribeAccountInfoUpdates().Wait();
        }
    }
}
