﻿using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Newtonsoft.Json;
using StockAnalyzer.Core;
using StockAnalyzer.Core.Domain;
using StockAnalyzer.Core.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace StockAnalyzer.CrossPlatform;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        IEX.PointerPressed += (e, a) => Open("https://iextrading.com/developer/");
        IEX_Terms.PointerPressed += (e, a) => Open("https://iextrading.com/api-exhibit-a/");

        /// Data provided for free by <a href="https://iextrading.com/developer/" RequestNavigate="Hyperlink_OnRequestNavigate">IEX</Hyperlink>. View <Hyperlink NavigateUri="https://iextrading.com/api-exhibit-a/" RequestNavigate="Hyperlink_OnRequestNavigate">IEX’s Terms of Use.</Hyperlink>
    }


    private static string API_URL = "https://ps-async.fekberg.com/api/stocks";
    private Stopwatch stopwatch = new Stopwatch();

    CancellationTokenSource? cancellationTokenSource;

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BeforeLoadingStockData();

            var data = await SearchForStocks();

            Stocks.ItemsSource = data.Where(price =>
                                price.Identifier == StockIdentifier.Text);
        }
        catch (Exception ex)
        {
            Notes.Text = ex.Message;
        }
        finally
        {
            AfterLoadingStockData();
        }
    }


    private Task<IEnumerable<StockPrice>> SearchForStocks()
    {
        var tcs = new TaskCompletionSource<IEnumerable<StockPrice>>();

        ThreadPool.QueueUserWorkItem(_ => {
            var lines = File.ReadAllLines("StockPrices_Small.csv");
            var prices = new List<StockPrice>();

            foreach (var line in lines.Skip(1))
            {
                prices.Add(StockPrice.FromCSV(line));
            }

            tcs.SetResult(prices);
        });

        return tcs.Task;
    }

    private async Task SearchForStocks(
        IProgress<IEnumerable<StockPrice>> progress)
    {
        var service = new StockService();
        var loadingTasks = new List<Task<IEnumerable<StockPrice>>>();

        foreach (var identifier in StockIdentifier.Text.Split(' ', ','))
        {
            var loadTask = service.GetStockPricesFor(identifier,
                CancellationToken.None);

            loadTask = loadTask.ContinueWith(completedTask =>
            {
                progress?.Report(completedTask.Result);
                return completedTask.Result;
            });

            loadingTasks.Add(loadTask);
        }

        var data = await Task.WhenAll(loadingTasks);

        Stocks.ItemsSource = data.SelectMany(stock => stock);
    }


    private async Task<IEnumerable<StockPrice>>
        GetStocksFor(string identifier)
    {
        var service = new StockService();
        var data = await service.GetStockPricesFor(identifier,
            CancellationToken.None).ConfigureAwait(false);

        return data.Take(5);
    }

    private static Task<List<string>> SearchForStocks(
        CancellationToken cancellationToken
    )
    {
        return Task.Run(async () =>
        {
            using var stream = new StreamReader(File.OpenRead("StockPrices_Small.csv"));

            var lines = new List<string>();

            while (await stream.ReadLineAsync() is string line)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                lines.Add(line);
            }

            return lines;
        }, cancellationToken);
    }

    private async Task GetStocks()
    {
        try
        {
            var store = new DataStore();

            var responseTask = store.GetStockPrices(StockIdentifier.Text);

            Stocks.ItemsSource = await responseTask;
        }
        catch (Exception ex)
        {
            throw;
        }
    }



    private void BeforeLoadingStockData()
    {
        stopwatch.Restart();
        StockProgress.IsVisible = true;
        StockProgress.IsIndeterminate = true;
    }

    private void AfterLoadingStockData()
    {
        StocksStatus.Text = $"Loaded stocks for {StockIdentifier.Text} in {stopwatch.ElapsedMilliseconds}ms";
        StockProgress.IsVisible = false;
    }

    private void Close_OnClick(object sender, RoutedEventArgs e)
    {
        if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            desktopLifetime.Shutdown();
        }
    }

    public static void Open(string url)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            url = url.Replace("&", "^&");
            Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.Start("xdg-open", url);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start("open", url);
        }
    }
}
