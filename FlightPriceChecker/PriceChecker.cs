using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System.Collections.ObjectModel;

namespace MathIA;

/// <summary>
///     Program settings.
/// </summary>
internal static class Settings
{
    /// <summary>
    ///     The departure and arrival airport codes.
    /// </summary>
    internal const string departure = "TPA", arrival = "JFK";

    /// <summary>
    ///     The # of days, from today, to search for flight prices.
    /// </summary>
    /// <remarks>
    ///     As of 7/14/23, flights can only be booked 330 days in advance.
    /// </remarks>
    internal const int daysToSearch = 30;

    /// <summary>
    ///     The amount of seconds to wait for the site to load.
    /// </summary>
    internal const int waitTime = 10;

    /// <summary>
    ///     The max number of times to try to gather flight data.
    /// </summary>
    internal const int attempts = 3;

    /// <summary>
    ///     The number of seconds to wait before retrying an operation.
    /// </summary>
    internal const int retryDelay = 2;

    /// <summary>
    ///     If the flight details should be printed in the console.
    /// </summary>
    internal const bool showResultsConsole = false;

    /// <summary>
    ///     The first departure date to search for.
    /// </summary>
    internal static DateTime StartDate => new DateTime(2024, 2, 10);

    /// <summary>
    ///     The path and name of the data file.
    /// </summary>
    internal const string filePath = @"D:\MathData17.csv";
}

/// <summary>
///     Gathers flight information and exports it to a .csv.
/// </summary>
class PriceChecker
{
    public static void Main(string[] args)
    {
        KayakChecker kayakChecker = new();
        kayakChecker.Start();
    }
}



/// <summary>
///     Scrapes kayak.com for flight prices.
/// </summary>
internal class KayakChecker
{
    /// <summary>
    ///     Starts the price checker.
    /// </summary>
    public void Start()
    {
        CsvHelper csvHelper = new();

        ChromeOptions options = new ChromeOptions();
        options.AddArguments("ignore-certificate-errors", "ignore-ssl-errors", "ignore-certificate-errors-spki-list");

        // Open new Chrome Window.
        IWebDriver driver = new ChromeDriver(options);

        DateTime searchDate = Settings.StartDate;
        int daysToSearch = Settings.daysToSearch;
        DateTime finalDate = searchDate.AddDays(daysToSearch);

        Console.WriteLine(
            string.Format(
                "--------------------------------------------------\n" +
                "Finding flights from {0} to {1},\n" +
                "starting on {2} til {3}\n" +
                "--------------------------------------------------",
                Settings.departure, Settings.arrival,
                searchDate.ToString("d"), finalDate.ToString("d")
                ));

        for (int i = 0; i < daysToSearch; i++)
        {
            FindFlights(driver, csvHelper, searchDate);
            searchDate = searchDate.AddDays(1);
        }

        driver.Quit();
        Console.WriteLine("Program has finished");
    }

    /// <summary>
    ///     Finds flight prices for the given date.
    /// </summary>
    /// <param name="driver">Chrome driver</param>
    /// <param name="csvHelper">csv helper</param>
    /// <param name="date">The date to find prices for.</param>
    private void FindFlights(IWebDriver driver, CsvHelper csvHelper, DateTime date)
    {
        Console.WriteLine(string.Format("Finding flights for {0}", date.ToString("d")));

        // Open Kayak with the appropriate query. 
        // For example: https://www.kayak.com/flights/TPA-JFK/2023-07-28?sort=price_a
        driver.Navigate().GoToUrl(
            string.Format(
                "https://www.kayak.com/flights/{0}-{1}/{2}?sort=price_a",
                Settings.departure, Settings.arrival, date.ToString("yyyy-MM-dd")
                ));

        // Wait for site to load.
        Thread.Sleep(TimeSpan.FromSeconds(Settings.waitTime));

        // "resultsList" will not load if no flights were found for 
        // the query date. We can use this to skip days for which
        // no flights were found.
        try
        {
            driver.FindElement(By.XPath("//div[@class=\"resultsList\"]"));
        }
        catch (OpenQA.Selenium.NoSuchElementException)
        {
            Console.WriteLine(
                string.Format("No flights found for {0}.", date.ToString("d")
                ));
            return;
        }

        // Attempt to gather flight information.
        int attempts = 0;
        int maxAttempts = Settings.attempts;
        while (attempts < maxAttempts)
        {
            Console.WriteLine(string.Format("Attempt #{0}", attempts));
            if (TryGetFlights(driver, csvHelper, date)) return;
            attempts++;
            Thread.Sleep(Settings.waitTime);
        }

        // Throw an error if unable to gather information.
        throw new Exception(
            string.Format("Unable to get flight data for {0}.", date.ToString("d")
            ));
    }

    /// <summary>
    ///     Tries to gather flight information.
    /// </summary>
    /// <returns>If it was successful.</returns>
    private bool TryGetFlights(IWebDriver driver, CsvHelper csvHelper, DateTime date)
    {
        List<FlightDetails> flights;
        FlightDetails flightDetails;
        DateTime time = DateTime.Now;

        try
        {
            ReadOnlyCollection<IWebElement> results = 
                driver.FindElements(
                    By.XPath(
                            // Find wrapper div with class "resultsList"
                            // Go down two wrapper divs
                            // All results are contained in divs with "data-resultid" attribute
                            "//div[@class=\"resultsList\"]/div/div/div[@data-resultid]"
                            )
                        );

            if (results.Count() == 0)
            {
                throw new Exception("No results found.");
            }

            flights = new();

            // Foreach Result
            foreach (IWebElement element in results)
            {
                flightDetails = new();

                flightDetails.DateCollected = time;

                // The airline name element is the only div with attribute "dir"
                IWebElement AirlineElement = element.FindElement(By.XPath(".//div[@dir]"));
                flightDetails.Airline = AirlineElement.Text;

                // The Depature and arrival time containers are siblings to the Airline element.
                ReadOnlyCollection<IWebElement> timeElements =
                    AirlineElement.FindElements(
                        By.XPath("preceding-sibling::div/*")
                        );

                // Find departure time.
                string departureTime = timeElements[0].Text;
                DateTime temp = DateTime.Parse(departureTime);
                flightDetails.DepartureTime = new(date.Year, date.Month, date.Day, temp.Hour, temp.Minute, 0);

                // Find arrival time.
                // If the arrival date is not the same as the departure date,
                // the arrival date will have a child <span> that states
                // the number of days the flight lasts.
                string arrivalTimeText = timeElements[2].Text;
                int index;
                try
                {
                    IWebElement timeChild = timeElements[2].FindElement(By.XPath("*"));

                    index = arrivalTimeText.IndexOf("+");
                    arrivalTimeText = arrivalTimeText[0..index];

                    string extraDays = timeChild.Text;
                    extraDays = extraDays[1..^0];

                    temp = DateTime.Parse(arrivalTimeText);
                    DateTime arrivalTime = new(date.Year, date.Month, date.Day, temp.Hour, temp.Minute, 0);
                    flightDetails.ArrivalTime = arrivalTime.AddDays(int.Parse(extraDays));

                }
                catch (OpenQA.Selenium.NoSuchElementException)
                {
                    temp = DateTime.Parse(timeElements[2].Text);
                    flightDetails.ArrivalTime = new(date.Year, date.Month, date.Day, temp.Hour, temp.Minute, 0);
                    flightDetails.StopCount = "+0";
                }

                string stops = AirlineElement.FindElement(By.XPath("../following-sibling::div/div[1]/span")).Text;
                flightDetails.StopCount = stops;

                ReadOnlyCollection<IWebElement> priceElements = element.FindElements(By.XPath(".//a"));
                flightDetails.Price = priceElements[0].FindElement(By.XPath("./div[1]/div[1]/div/div")).Text;
                if (!flightDetails.Airline.Equals("Amtrak"))
                {
                    flightDetails.Cabin = priceElements[0].FindElement(By.XPath("../..//div[@title]")).Text;
                }

                flights.Add(flightDetails);

                if (priceElements.Count > 3)
                {
                    ReadOnlyCollection<IWebElement> extraFlights;
                    for (int i = 3; i < priceElements.Count; i++)
                    {
                        flightDetails = new FlightDetails(flightDetails);
                        extraFlights = priceElements[i].FindElements(By.XPath("./div/div"));
                        flightDetails.Price = extraFlights[0].Text;
                        if(flightDetails.Price.Equals("View Deal"))
                        {
                            break;
                        }
                        flightDetails.Cabin = extraFlights[1].Text;
                        flights.Add(flightDetails);
                    }
                }
            }
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine(
                string.Format(
                    "{0} Results Found for {1}",
                    flights.Count.ToString(), date.ToString("d")
                    ));
            Console.ResetColor();

            if (Settings.showResultsConsole)
            {
                foreach (FlightDetails details in flights)
                {
                    Console.WriteLine("-----------------------------------------------------------------");
                    Console.WriteLine("Airline: " + details.Airline);
                    Console.WriteLine("departure: " + details.DepartureTime.ToString());
                    Console.WriteLine("arrival: " + details.ArrivalTime.ToString());
                    Console.WriteLine("stop count: " + details.StopCount);
                    Console.WriteLine("Price: " + details.Price);
                    Console.WriteLine("Cabin: " + details.Cabin);
                    Console.WriteLine("-----------------------------------------------------------------");
                }
            }

            csvHelper.AppendToCsv(flights);
            return true;

        }
        catch (OpenQA.Selenium.StaleElementReferenceException)
        {
            Console.WriteLine("Stale Element Reference Exception");
            return false;
        }
        catch (OpenQA.Selenium.WebDriverException)
        {
            Console.WriteLine("WebDriverException");
            return false;
        }
    }
}

/// <summary>
///     Writes data to .csv.
/// </summary>
internal class CsvHelper
{
    /// <summary>
    ///     The path and name of the data file.
    /// </summary>
    private readonly string filePath;

    /// <summary>
    ///     Checks if the file exists, and creates it if it doesn't.
    /// </summary>
    internal void CheckForFile()
    {
        if (!File.Exists(filePath))
        {
            File.Create(filePath).Close();
            File.WriteAllTextAsync(filePath,
                "\"Date Collected\",\"Airline\",\"Departure Time\",\"Arrival Time\",\"Number of Stops\",\"Price\",\"Cabin Class\""
                );
        }
    }

    /// <summary>
    ///     Appends data to the csv.
    /// </summary>
    /// <param name="flights">The flight information.</param>
    internal void AppendToCsv(List<FlightDetails> flights)
    {
        foreach (FlightDetails flightDetails in flights)
        {
            while (true)
            {
                // C# sometimes does not close the filestream right away.
                // The program will try to append and if it fails, it will
                // wait 1 second and try again. 
                try
                {
                    File.AppendAllText(filePath, string.Format(
                    "\n\"{0}\",\"{1}\",\"{2}\",\"{3}\",\"{4}\",\"{5}\",\"{6}\"",
                    flightDetails.DateCollected.ToString("g"), flightDetails.Airline,
                    flightDetails.DepartureTime.ToString("g"), flightDetails.ArrivalTime.ToString("g"),
                    flightDetails.StopCount, flightDetails.Price, flightDetails.Cabin
                    ));
                    break;
                }
                catch (System.IO.IOException)
                {
                    Console.WriteLine("Writing to the .csv failed, trying again...");
                    Thread.Sleep(1000);
                }
            }
        }
        Console.WriteLine(
            string.Format("Successfully appended data for {0}.", flights[0].DepartureTime.ToString("d"))
            );
    }

    /// <summary>
    ///     Instantiates the class and makes it write to the provided file.
    /// </summary>
    /// <param name="filePath">The path for the .csv file.</param>
    internal CsvHelper()
    {
        this.filePath = Settings.filePath;
        CheckForFile();
    }
}

/// <summary>
///     Holds details regarding flights.
/// </summary>
internal class FlightDetails
{
    /// <summary>
    ///     The time at which the data was collected.
    /// </summary>
    public DateTime DateCollected { get; set; }

    /// <summary>
    ///     The airline that offers the flight.
    /// </summary>
    public string Airline { get; set; }

    /// <summary>
    ///     The departure time.
    /// </summary>
    public DateTime DepartureTime { get; set; }

    /// <summary>
    ///     The arrival time.
    /// </summary>
    public DateTime ArrivalTime { get; set; }

    /// <summary>
    ///     The number of stops/layovers.
    /// </summary>
    public string StopCount { get; set; }

    /// <summary>
    ///     The price of the ticiket.
    /// </summary>
    public string Price { get; set; }

    /// <summary>
    ///     The Cabin Class.
    /// </summary>
    public string Cabin { get; set; }

    // Avoid CS8618
    public FlightDetails()
    {
        Airline = string.Empty;
        StopCount = string.Empty;
        Price = string.Empty;
        Cabin = string.Empty;
    }

    /// <summary>
    ///     Duplicates the instance.
    /// </summary>
    /// <param name="details">The instance to duplicate.</param>
    public FlightDetails(FlightDetails details)
    {
        this.DateCollected = details.DateCollected;
        this.Airline = details.Airline;
        this.DepartureTime = details.DepartureTime;
        this.ArrivalTime = details.ArrivalTime;
        this.StopCount = details.StopCount;
        this.Price = details.Price;
        this.Cabin = details.Cabin;
    }
}