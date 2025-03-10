using static System.Net.Mime.MediaTypeNames;
using System.Data;
using System.Text.Json;
using System.Net.Mail;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using RestSharp;
using System.Text;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Util.Store;
using Microsoft.IdentityModel.Protocols;
using System.Net.Http.Headers;
using System;
using static Jadlam_ProductsUpdater.Program;


namespace Jadlam_ProductsUpdater
{
    class Program
    {
        #region GLOBAL VARIABLES
        static string path = AppDomain.CurrentDomain.BaseDirectory +"/ErrorLogs/";
        static string logFilePath = AppDomain.CurrentDomain.BaseDirectory + "/process_log.txt";
        static string accesstoken = string.Empty;
        static string connectionString = "Server=51.143.185.157; Database=Jadlamstaging; Integrated Security=false; User ID=admins; Password=Inform@2020*; Column Encryption Setting=enabled; TrustServerCertificate=True; Connection Timeout=3600";
        static DataTable dt = new DataTable();
        static DataTable dataasdacin = new DataTable();

        #endregion
        static async Task Main(string[] args)
        {
            try
            {
                //  ---------------------------------------Clear the log file at startup -------------------------------------------
                File.WriteAllText(logFilePath, string.Empty);

                //-------------------------------------------------------------------------------------------------------------------------------------
                //----------------------------------CREATING DATATABLE TO ADD DATA IN DATABASE---------------------------------------------------------
                //-------------------------------------------------------------------------------------------------------------------------------------

                dt.Columns.Add("ID", typeof(int));
                dt.Columns.Add("EAN", typeof(string));
                dt.Columns.Add("Sku", typeof(string));
                dt.Columns.Add("Title", typeof(string));
                dt.Columns.Add("WarehouseLocation", typeof(string));
                dt.Columns.Add("ProductType", typeof(string));
                dt.Columns.Add("ParentSku", typeof(string));
                dt.Columns.Add("RetailPrice", typeof(string));
                dt.Columns.Add("PackageType", typeof(string));
                dt.Columns.Add("AttEAN", typeof(string));
                dt.Columns.Add("ProfileId", typeof(string));
                

                //-------------------------------------------------------------------------------------------------------------------------------------
                //--------------------------------------------GETTING THE LAST EXECUTION DATE----------------------------------------------------------
                //-------------------------------------------------------------------------------------------------------------------------------------
                string textFile = @"F:\Jadlam core\Jadlam_ProductsUpdater\bin\Debug\net8.0\LastExecustionDateFile\executionDate.txt";

                string text = string.Empty;
                if (File.Exists(textFile))
                {
                    text = File.ReadAllText(textFile);
                }
               
                var streamWriter = new StreamWriter(textFile, false);
                DateTime inputDate = DateTime.ParseExact(DateTime.UtcNow.ToString(), "M/d/yyyy h:mm:ss tt", null);
                // Format the date in the desired output format
                string outputDateString = inputDate.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ");

                streamWriter.Write(outputDateString);
                streamWriter.Close();

                List<(string message, string query, string url)> urlList = new List<(string, string, string)>
                 {
                        ("Product Ids less than or equal to 5000000","select * from JADLAM_EAN_SKU_MAPPING  WITH (NOLOCK) where id<= 5000000  and ProfileId=73000354","https://api.channeladvisor.com/v1/Products?&$filter=ProfileId  eq 73000354 and ID le 5000000 and UpdateDateUtc ge " + text + " &$select=ID,ProfileID,Sku,EAN,Title,WarehouseLocation,ProductType,ParentSku,RetailPrice&$expand=Attributes($filter= Name eq 'Packaging Type' or Name eq 'BC-EAN(SHOP)')&$count=true"),
                        ("Product Ids greater than  5000000 and less than or equal to 5060000","select * from JADLAM_EAN_SKU_MAPPING  with (nolock) where id > 5000000 and id <= 5060000 and profileid=73000354","https://api.channeladvisor.com/v1/Products?&$filter=ProfileId  eq 73000354 and ID gt 5000000 and ID le 5060000  and UpdateDateUtc ge " + text + " &$select=ID,ProfileID,Sku,EAN,Title,WarehouseLocation,ProductType,ParentSku,RetailPrice&$expand=Attributes($filter= Name eq 'Packaging Type' or Name eq 'BC-EAN(SHOP)')&$count=true"),
                        ("Product Ids greater than 5060000 ","select * from JADLAM_EAN_SKU_MAPPING  with (nolock) where id > 5060000 and profileid=73000354","https://api.channeladvisor.com/v1/Products?&$filter=ProfileId  eq 73000354 and ID gt 5060000 and UpdateDateUtc ge " + text + " &$select=ID,ProfileID,Sku,EAN,Title,WarehouseLocation,ProductType,ParentSku,RetailPrice&$expand=Attributes($filter= Name eq 'Packaging Type' or Name eq 'BC-EAN(SHOP)')&$count=true"),
                        ("Products of HSB Account","select * from JADLAM_EAN_SKU_MAPPING  WITH (NOLOCK) where ProfileId=73000847","https://api.channeladvisor.com/v1/Products?&$filter=ProfileId  eq 73000847 and UpdateDateUtc ge " + text + "  &$select=ID,ProfileID,Sku,EAN,Title,WarehouseLocation,ProductType,ParentSku,RetailPrice&$expand=Attributes($filter= Name eq 'Packaging Type' or Name eq 'BC-EAN(SHOP)')&$count=true")
                 };

                foreach (var (message, query, url) in urlList)
                {
                    //-------------------------------------------------------------------------------------------------------------------------------------
                    //---------------------------------- GETTING ACCESS TOKEN FROM CHANNEL ADVISOR---------------------------------------------------------
                    //-------------------------------------------------------------------------------------------------------------------------------------
                    Accesstoken accesstokens = GetAccessToken();
                    string accesstoken = accesstokens.access_token;

                    string fullUrl = url + "&access_token=" + accesstoken;
                    //-------------------------------------------------------------------------------------------------------------------------------------
                    //----------------------------------  PROCESSING URL TO GET THE PRODUCT DETAIL  ---------------------------------------------------------
                    //-------------------------------------------------------------------------------------------------------------------------------------
                    var (success, errorMessage) = await ProcessUrl(message,query, fullUrl);
                    if (!success)
                    {
                        var (retrySuccess, retryErrorMessage) = await ProcessUrl(message,query, fullUrl, true);
                        if (!retrySuccess)
                        {
                            LogToFile($"{message} || after retry find exception: {retryErrorMessage}");
                        }
                    }
                   
                }

            }
            catch ( Exception ex)
            {
                Log("Error in Jadlam_ProductsUpdater:  " + ex.Message);

                // Read log file content before sending the email
                string logContent = File.Exists(logFilePath) ? File.ReadAllText(logFilePath) : "No log file created.";

                // Format log content with <br> for email formatting
                string formattedLogContent = logContent.Replace(Environment.NewLine, "<br>");

                // Send email with log details
                Sendmail("Error in Jadlam_ProductUpdater", $"{ex.Message}<br><br>{formattedLogContent}");
            }
        }
        static async Task<(bool success, string errorMessage)> ProcessUrl(string message, string query, string url, bool isRetry = false)
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {

                try
                {
                    //-------------------------------------------------------------------------------------------------------------------------------------
                    //--------------------------------------------GETTING COMPLETE DATA FROM DATABASE------------------------------------------------------
                    //-------------------------------------------------------------------------------------------------------------------------------------
                    using (SqlConnection con = new SqlConnection(connectionString))
                    {
                        //Set deadlock priority for the session
                        using (SqlCommand setDeadlockPriorityCommand = new SqlCommand("SET DEADLOCK_PRIORITY -5", con))
                        {
                            if (con.State == ConnectionState.Closed)
                                con.Open();
                            setDeadlockPriorityCommand.ExecuteNonQuery();
                            con.Close();
                        }

                        using (SqlCommand cmd = new SqlCommand(query))
                        {
                            cmd.Connection = con;
                            if (con.State == ConnectionState.Closed)
                                con.Open();
                            cmd.CommandType = CommandType.Text;
                            SqlDataAdapter adp = new SqlDataAdapter(cmd);
                            adp.Fill(dataasdacin);
                        }
                    }
                    //-------------------------------------------------------------------------------------------------------------------------------------
                    //----------------------------------------------GETTING DETAIL FROM CHANNEL -----------------------------------------------------------
                    //-------------------------------------------------------------------------------------------------------------------------------------

                    List<Value> products = await GetOrderDataprd(url);

                    if (products != null)
                    {
                        foreach (var item in products)
                        {
                            string packageType = "";
                            string attEAN = "";

                            // Extract Attributes (PackageType and AttEAN)
                            if (item.Attributes != null)
                            {
                                foreach (var attr in item.Attributes)
                                {
                                    if (attr.Name == "Packaging Type")
                                        packageType = attr.Value;
                                    else if (attr.Name == "BC-EAN(SHOP)")
                                        attEAN = attr.Value;
                                }
                            }

                            dt.Rows.Add(
                                item.ID,

                                item.EAN,
                                item.Sku,
                                item.Title,
                                item.WarehouseLocation,
                                item.ProductType,
                                item.ParentSku,
                                packageType,
                                attEAN,
                                item.ProfileID,
                            item.RetailPrice
                            );

                        }
                        DataColumn[] columns = dataasdacin.Columns.Cast<DataColumn>().ToArray();

                        if (dt != null)
                        {


                            using (SqlConnection con = new SqlConnection(connectionString))
                            {
                                con.Open();

                                using (SqlCommand cmd = new SqlCommand("sp_JADLAM_EAN_SKU_MAPPING", con))
                                {
                                    cmd.CommandType = CommandType.StoredProcedure;

                                    // Pass DataTable as a parameter
                                    SqlParameter tableParam = cmd.Parameters.AddWithValue("@TempTable", dt);
                                    tableParam.SqlDbType = SqlDbType.Structured; // Set type as Structured (Table-Valued Parameter)
                                    tableParam.TypeName = "dbo.JADLAM_EAN_SKU_MAPPING"; // Ensure exact schema name and type name

                                    cmd.ExecuteNonQuery();
                                }

                            }

                        }

                        LogToFile($"{message} || done");
                        return (true, string.Empty);
                    }
                    else
                    {
                        Log("Failed to retrieve data from API.");
                        return (false, "No data found.");

                    }
                }
                catch (SqlException ex) when (IsTransientError(ex))
                {
                    Console.WriteLine($"Transient error occurred. Attempt {attempt} of {3}. Retrying in {5000 / 1000} seconds...");
                    Thread.Sleep(5000);
                }
                catch (Exception ex)
                {
                    string errorMessage = $"{message} || Exception: {ex.Message}";
                    LogToFile(errorMessage);
                    return (false, errorMessage);
                }
            }
           string errorMessage1 = $"{message} || ProcessUrl failed after 3 attempts.";
            LogToFile(errorMessage1);
            return (false, errorMessage1);
        }
         #region GETTING PRODUCTS FOR MAPPING FROM CHANNEL
        static async Task<List<Value>> GetOrderDataprd(string URL)

        {
            List<Value> allproducts = new List<Value>();

            try
            {
                using (HttpClient client = new HttpClient())
                {
                   
                   
                    client.Timeout = TimeSpan.FromSeconds(40);  // Increase timeout to 30 seconds

                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    while (!string.IsNullOrEmpty(URL))
                    {
                       
                        // Make the API call
                        HttpResponseMessage response = await client.GetAsync(URL);

                        if (response.IsSuccessStatusCode)
                        {
                         
                            string apiResponse = await response.Content.ReadAsStringAsync();
                            Product productResponse = JsonConvert.DeserializeObject<Product>(apiResponse);

                            // Print response for debugging (optional)
                            Console.WriteLine("API Response: " + apiResponse);

                            // Deserialize the response into the ProductResponse object
 

                            allproducts.AddRange(productResponse.value);

                            // Update the URL with the nextLink if available
                             URL = productResponse.OdataNextLink;
                            //URL = null;
                        }
                        else
                        {
                            // Log and return null if retries are exhausted
                            Log($"Invalid Status Code Failed to retrieve product details in Jadlam_FetchOrdersForPicklist. Status: {response.StatusCode}, Reason: {response.ReasonPhrase}");
                            return null;
                        }
                    }
                }
                // Return product list
                return allproducts; // Assuming ProductResponse has a list of Products
            }
            catch (Exception ex)
            {
                // Log the error and return null if retries are exhausted
                Log($"Error retrieving product details for Product ID in Jadlam_FetchOrdersForPicklist: {ex.Message}");
                return null;
            }
        }


        #endregion

        static bool IsTransientError(SqlException ex)
        {
            int[] transientErrorNumbers = { 40613, 40197, 40501, 10928, 10929, 11001, 233 };
            return ex.Errors.Cast<SqlError>().Any(error => transientErrorNumbers.Contains(error.Number));
        }
        #region GETTING ACCESS TOKEN
        static Accesstoken GetAccessToken()
        {
            string URL = "oauth2/token";
            string refreshtoken = "bsNYTWti-r8BttlV-cf7DL_Vf3QxDYpHHNE4iQv6iaE";
            string applicationid = "aka7vw99tpzu12igo9x3x9ty18mkdw94";
            string secretid = "BeIncReW4keeG4P9YjpfrA";
            string authorize = applicationid + ":" + secretid;
            string encode = EncodeTo64(authorize);
            string encodeauthorize = "Basic " + encode;
            Accesstoken accesstoken = PostForAccesstoken(URL, refreshtoken, encodeauthorize);
            return accesstoken;
        }
        static Accesstoken PostForAccesstoken(string URL, string refreshtoken, string encodeauthorize)
        {
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls11 | System.Net.SecurityProtocolType.Tls12;
            string URI = "https://api.channeladvisor.com/" + URL;

            var client = new RestClient(URI);
            var request = new RestRequest(); // Correct constructor
            request.Method = Method.Post;   // Set the method explicitly

            // Add headers
            request.AddHeader("Authorization", encodeauthorize);
            request.AddHeader("Content-Type", "application/x-www-form-urlencoded");
            request.AddHeader("Cache-Control", "no-cache");

            // Add parameters
            request.AddParameter("grant_type", "refresh_token");
            request.AddParameter("refresh_token", refreshtoken);

            // Execute request
            var response = client.Execute<Accesstoken>(request);
            return response.Data; // RestSharp handles deserialization automatically
        }


        static string EncodeTo64(string toEncode)
        {
            byte[] toEncodeAsBytes = System.Text.ASCIIEncoding.ASCII.GetBytes(toEncode);
            string returnValue = System.Convert.ToBase64String(toEncodeAsBytes);
            return returnValue;
        }
        private static void Log(string message)
        {
            string logFilePath = @"F:\Jadlam core\Jadlam_FetchOrdersForPicklist\bin\Debug\net8.0\Logs\" + DateTime.Now.ToString("yyyyMMdd") + ".txt";
            Directory.CreateDirectory(Path.GetDirectoryName(logFilePath));
            using (StreamWriter writer = new StreamWriter(logFilePath, true))
            {
                writer.WriteLine($"[{DateTime.Now}] {message}");
            }
        }
        static void LogToFile(string logMessage)
        {
            File.AppendAllText(logFilePath, logMessage + Environment.NewLine);
        }
        #endregion
        #region Sendmail
        public static bool Sendmail(string subject, string body)
        {

            try
            {
                // Initialize Gmail API service
                var service = InitializeGmailService();



                // Create an instance of EmailHelper
                var emailHelper2 = new EmailHelper();

                // Create an email message using the instance
                var message2 = emailHelper2.CreateEmail("ramandeep.matrid33789@gmail.com", "support@weblegs.co.uk", subject + DateTime.Now.ToString("dddd, dd MMMM yyyy HH: mm"), body);

                // Send the email via Gmail API using the instance
                emailHelper2.SendMessage(service, "me", message2);


                // Create an instance of EmailHelper
                var emailHelper3 = new EmailHelper();

                //Create an email message using the instance
                var message3 = emailHelper3.CreateEmail("medfosys.186@gmail.com", "support@weblegs.co.uk", subject + DateTime.Now.ToString("dddd, dd MMMM yyyy HH: mm"), body);

                // Send the email via Gmail API using the instance
                emailHelper3.SendMessage(service, "me", message3);
                // Create an instance of EmailHelper
                var emailHelper4 = new EmailHelper();

                // Create an email message using the instance
                var message4 = emailHelper4.CreateEmail("kamal.matrid77991@gmail.com", "support@weblegs.co.uk", subject + DateTime.Now.ToString("dddd, dd MMMM yyyy HH: mm"), body);

                // Send the email via Gmail API using the instance
                emailHelper4.SendMessage(service, "me", message4);

            }
            catch (Exception ex)
            {
                Log(body);

                Console.WriteLine("An error occurred: " + ex.Message);
                return false;
            }
            return true;
        }
        // Method to initialize the Gmail API service
        private static GmailService InitializeGmailService()
        {
            UserCredential credential;

            // Correct path separator for credentials.json
            var credPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "credentials.json");

            using (var stream = new FileStream(credPath, FileMode.Open, FileAccess.Read))
            {
                //string tokenPath = "token.json";
                string tokenPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "token.json");
                credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.Load(stream).Secrets,
                    new[] { GmailService.Scope.GmailSend },
                    "user",
                    CancellationToken.None,
                    new FileDataStore(tokenPath, true)).Result;

                Console.WriteLine("Credential file saved to: " + tokenPath);
            }

            return new GmailService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "Gmail API .NET Quickstart",
            });
        }

        // Helper class for creating and sending the email
        public class EmailHelper
        {
            public Google.Apis.Gmail.v1.Data.Message CreateEmail(string to, string from, string subject, string bodyText, string attachmentFilePath = null)
            {
                var rawEmail = new StringBuilder();
                rawEmail.AppendLine($"To: {to}");
                rawEmail.AppendLine($"From: {from}");
                rawEmail.AppendLine($"Subject: {subject}");
                rawEmail.AppendLine("Content-Type: multipart/mixed; boundary=\"boundary\"");
                rawEmail.AppendLine();
                rawEmail.AppendLine("--boundary");
                rawEmail.AppendLine("Content-Type: text/html; charset=utf-8");
                rawEmail.AppendLine("Content-Transfer-Encoding: quoted-printable");
                rawEmail.AppendLine();
                rawEmail.AppendLine(bodyText);

                if (!string.IsNullOrEmpty(attachmentFilePath))
                {
                    var fileBytes = File.ReadAllBytes(attachmentFilePath);
                    var fileContent = Convert.ToBase64String(fileBytes)
                        .Replace('+', '-')
                        .Replace('/', '_')
                        .Replace("=", "");

                    string fileName = Path.GetFileName(attachmentFilePath);

                    rawEmail.AppendLine("--boundary");
                    rawEmail.AppendLine($"Content-Type: application/octet-stream; name=\"{fileName}\"");
                    rawEmail.AppendLine($"Content-Disposition: attachment; filename=\"{fileName}\"");
                    rawEmail.AppendLine("Content-Transfer-Encoding: base64");
                    rawEmail.AppendLine();
                    rawEmail.AppendLine(fileContent);
                }

                rawEmail.AppendLine("--boundary--");

                var email = new Google.Apis.Gmail.v1.Data.Message
                {
                    Raw = Base64UrlEncode(rawEmail.ToString())
                };
                return email;
            }
            private string Base64UrlEncode(string input)
            {
                var inputBytes = Encoding.UTF8.GetBytes(input);
                return Convert.ToBase64String(inputBytes)
                  .Replace('+', '-')
                  .Replace('/', '_')
                  .Replace("=", "");
            }

            public void SendMessage(GmailService service, string userId, Google.Apis.Gmail.v1.Data.Message email)
            {
                try
                {
                    service.Users.Messages.Send(email, userId).Execute();
                    Console.WriteLine("Email sent successfully.");
                }
                catch (Exception e)
                {
                    Console.WriteLine("An error occurred: " + e.Message);
                    throw;
                }
            }
        }
        #endregion

        #region CLASSES
        public class Accesstoken
        {
            public string access_token { get; set; }
            public string token_type { get; set; }
            public int expires_in { get; set; }
        }

        public class Product
        {
            [JsonProperty("@odata.context")]
            public string OdataContext { get; set; }
            public List<Value> value { get; set; }

            [JsonProperty("@odata.nextLink")]
            public string OdataNextLink { get; set; }

            [JsonProperty("@odata.count")]
            public int OdataCount { get; set; }
        }

        public class invet2
        {
            [JsonProperty("@odata.context")]
            public string OdataContext { get; set; }
            public List<Value> value { get; set; }

            [JsonProperty("@odata.count")]
            public int OdataCount { get; set; }
        }
        public class Value
        {
            public int ID { get; set; }
            public int ProfileID { get; set; }
            public string EAN { get; set; }
            public string Sku { get; set; }
            public string Title { get; set; }
            public string WarehouseLocation { get; set; }
            public string ProductType { get; set; }
            public string ParentSku { get; set; }
            public string RetailPrice { get; set; }
            public List<Attribute> Attributes { get; set; }
        }
        public class Attribute
        {
            public int ProductID { get; set; }
            public int ProfileID { get; set; }
            public string Name { get; set; }
            public string Value { get; set; }
        }
       
        #endregion
    }
}
#region old

////UPDATE 1: 
//using (connection)
//{
//    //Set deadlock priority for the session
//    using (SqlCommand setDeadlockPriorityCommand = new SqlCommand("SET DEADLOCK_PRIORITY -5", connection))
//    {
//        if (connection.State == ConnectionState.Closed)
//            connection.Open();
//        setDeadlockPriorityCommand.ExecuteNonQuery();
//        connection.Close();
//    }
//    using (SqlCommand cmd = new SqlCommand("select * from JADLAM_EAN_SKU_MAPPING_testing  WITH (NOLOCK) where id<= 5000000  and ProfileId=73000354"))
//    {
//        cmd.Connection = connection;
//        if (connection.State == ConnectionState.Closed)
//            connection.Open();
//        cmd.CommandType = CommandType.Text;
//        SqlDataAdapter adp = new SqlDataAdapter(cmd);
//        adp.Fill(dataasdacin);
//    }
//    connection.Close();
//}

////UPDATE 2: 
//using (connection)
//{
//    //Set deadlock priority for the session
//    using (SqlCommand setDeadlockPriorityCommand = new SqlCommand("SET DEADLOCK_PRIORITY -5", connection))
//    {
//        if (connection.State == ConnectionState.Closed)
//            connection.Open();
//        setDeadlockPriorityCommand.ExecuteNonQuery();
//        connection.Close();
//    }

//    using (SqlCommand cmd = new SqlCommand("select * from JADLAM_EAN_SKU_MAPPING_testing  with (nolock) where id > 5000000 and id <= 5060000 and profileid=73000354"))
//    {
//        cmd.Connection = connection;
//        if (connection.State == ConnectionState.Closed)
//            connection.Open();
//        cmd.CommandType = CommandType.Text;
//        SqlDataAdapter adp = new SqlDataAdapter(cmd);
//        adp.Fill(dataasdacin);
//    }
//}


//// UPDATE 3: 
//using (connection)
//{
//    // Set deadlock priority for the session
//    using (SqlCommand setDeadlockPriorityCommand = new SqlCommand("SET DEADLOCK_PRIORITY -5", connection))
//    {
//        if (connection.State == ConnectionState.Closed)
//            connection.Open();
//        setDeadlockPriorityCommand.ExecuteNonQuery();
//    }
//    using (SqlCommand cmd = new SqlCommand("select * from JADLAM_EAN_SKU_MAPPING_testing  WITH (NOLOCK) where ProfileId=73000847"))
//    {
//        cmd.Connection = connection;
//        if (connection.State == ConnectionState.Closed)
//            connection.Open();
//        cmd.CommandType = CommandType.Text;
//        SqlDataAdapter adp = new SqlDataAdapter(cmd);
//        adp.Fill(dataasdacin);
//    }
//    connection.Close();
//}


//// UPDATE 4: 
//using (connection)
//{
//    //Set deadlock priority for the session
//    using (SqlCommand setDeadlockPriorityCommand = new SqlCommand("SET DEADLOCK_PRIORITY -5", connection))
//    {
//        if (connection.State == ConnectionState.Closed)
//            connection.Open();
//        setDeadlockPriorityCommand.ExecuteNonQuery();
//        connection.Close();
//    }

//    using (SqlCommand cmd = new SqlCommand("select * from JADLAM_EAN_SKU_MAPPING_testing  with (nolock) where id > 5060000 and profileid=73000354"))
//    {
//        cmd.Connection = connection;
//        if (connection.State == ConnectionState.Closed)
//            connection.Open();
//        cmd.CommandType = CommandType.Text;
//        SqlDataAdapter adp = new SqlDataAdapter(cmd);
//        adp.Fill(dataasdacin);
//    }
//}

//-------------------------------------------------------------------------------------------------------------------------------------
//----------------------------------------------GETTING DETAIL FROM CHANNEL -----------------------------------------------------------
//-------------------------------------------------------------------------------------------------------------------------------------


//PART 1 - UPDATE
//string Url = "https://api.channeladvisor.com/v1/Products?access_token=" + accesstoken + "&$filter=ProfileId  eq 73000354 and ID le 5000000 and UpdateDateUtc ge " + text + " &$select=ID,Sku,EAN,Title,WarehouseLocation,ProductType,ParentSku,RetailPrice&$expand=Attributes($filter= Name eq 'Packaging Type' or Name eq 'BC-EAN(SHOP)')&$count=true";

//PART 2 - UPDATE
// string Url = "https://api.channeladvisor.com/v1/Products?access_token=" + accesstoken + "&$filter=ProfileId  eq 73000354 and ID gt 5000000 and ID le 5060000  and UpdateDateUtc ge " + text + " &$select=ID,Sku,EAN,Title,WarehouseLocation,ProductType,ParentSku,RetailPrice&$expand=Attributes($filter= Name eq 'Packaging Type' or Name eq 'BC-EAN(SHOP)')&$count=true";

//PART 3 - UPDATE (HSB LINK)
// string Url = "https://api.channeladvisor.com/v1/Products?access_token=" + accesstoken + "&$filter=ProfileId  eq 73000847 and UpdateDateUtc ge " + text + "  &$select=ID,Sku,EAN,Title,WarehouseLocation,ProductType,ParentSku,RetailPrice&$expand=Attributes($filter= Name eq 'Packaging Type' or Name eq 'BC-EAN(SHOP)')&$count=true";

//PART 4 - UPDATE
//string Url = "https://api.channeladvisor.com/v1/Products?access_token=" + accesstoken + "&$filter=ProfileId  eq 73000354 and ID gt 5060000  &$select=ID,Sku,EAN,Title,WarehouseLocation,ProductType,ParentSku,RetailPrice&$expand=Attributes($filter= Name eq 'Packaging Type' or Name eq 'BC-EAN(SHOP)')&$count=true";

//List<Value> products = await GetOrderDataprd(Url);

//if (products != null)
//{
//    foreach (var item in products)
//    {
//        string packageType = "";
//        string attEAN = "";

//        // Extract Attributes (PackageType and AttEAN)
//        if (item.Attributes != null)
//        {
//            foreach (var attr in item.Attributes)
//            {
//                if (attr.Name == "Packaging Type")
//                    packageType = attr.Value;
//                else if (attr.Name == "BC-EAN(SHOP)")
//                    attEAN = attr.Value;
//            }
//        }

//        dt.Rows.Add(
//            item.ID,
//            item.EAN,
//            item.Sku,
//            item.Title,
//            item.WarehouseLocation,
//            item.ProductType,
//            item.ParentSku,
//            packageType,
//            attEAN,
//             //73000354,
//             73000847,  // for HSB 
//        item.RetailPrice
//        );
//    }

//    Console.WriteLine("DataTable filled successfully with API data.");
//}
//else
//{
//    Console.WriteLine("Failed to retrieve data from API.");
//}

//DataColumn[] columns = dataasdacin.Columns.Cast<DataColumn>().ToArray();

//if (dt != null)
//{
//    connection.ConnectionString = connetionString;

//    using (connection)
//    {
//        connection.Open();

//        using (SqlCommand cmd = new SqlCommand("sp_JADLAM_EAN_SKU_MAPPING", connection))
//        {
//            cmd.CommandType = CommandType.StoredProcedure;

//            // Pass DataTable as a parameter
//            SqlParameter tableParam = cmd.Parameters.AddWithValue("@TempTable", dt);
//            tableParam.SqlDbType = SqlDbType.Structured; // Set type as Structured (Table-Valued Parameter)
//            tableParam.TypeName = "dbo.JADLAM_EAN_SKU_MAPPING"; // Ensure exact schema name and type name

//            cmd.ExecuteNonQuery();
//        }

//    }

//}
// static string  GetOrderDataprd(string URL)
//{
//    inventory.Clear();
//    string res = string.Empty;
//    using (var client = new HttpClient())
//    {
//        var response = client.GetAsync(URL).Result;
//        if (response.IsSuccessStatusCode)
//        {
//            Task<invet> result = null;
//            Task<invet2> result2 = null;
//            if (finalTotalCount != 0)
//            {
//                if ((finalTotalCount) - totalCount >= 100)
//                {
//                    result = response.Content.ReadAsAsync<invet>();
//                }
//                else
//                {
//                    result2 = response.Content.ReadAsAsync<invet2>();
//                }
//            }
//            else
//            {
//                result = response.Content.ReadAsAsync<invet>();

//            }
//            if (endCount == 0)
//            {
//                finalTotalCount = result.Result.OdataCount;
//                endCount = result.Result.OdataCount % 100;
//            }
//            if (finalTotalCount != 0)
//            {
//                if ((finalTotalCount) - totalCount >= 100)
//                {
//                    inventory = inventory.Concat(result.Result.value).ToList();
//                    inventorydata = inventory;
//                    if (!string.IsNullOrEmpty(result.Result.OdataNextLink))
//                    {
//                        #region ADDING DATA TO DATABASE
//                        string json = Newtonsoft.Json.JsonConvert.SerializeObject(inventory);


//                        foreach (var i in inventory)
//                        {
//                            var row = dt.NewRow();
//                            row["ID"] = i.ID;
//                            row["EAN"] = i.EAN;
//                            row["Sku"] = i.Sku;
//                            row["Title"] = i.Title;
//                            row["WarehouseLocation"] = i.WarehouseLocation;
//                            row["ProductType"] = i.ProductType;
//                            row["ParentSku"] = i.ParentSku;
//                            row["RetailPrice"] = i.RetailPrice;
//                            //  row["ProfileId"] = "73000847";// HSB
//                            row["ProfileId"] = "73000354";
//                            if (i.Attributes.Any())
//                            {
//                                foreach (var att in i.Attributes)
//                                {
//                                    if (att.Name == "Packaging Type")
//                                    {
//                                        row["PackageType"] = att.Value;
//                                    }
//                                    if (att.Name == "BC-EAN(SHOP)")
//                                    {
//                                        row["AttEAN"] = att.Value;
//                                    }
//                                }
//                            }
//                            dt.Rows.Add(row);
//                        }
//                        totalCount = totalCount + inventory.Count;

//                        #endregion
//                        if (inventory.Count >= refreshtokencount)
//                        {
//                            //SendErrorMail("Token Refreshed");
//                            string oldtoken = accesstoken;
//                            Accesstoken accesstokens = GetAccessToken();
//                            accesstoken = accesstokens.access_token;
//                            result.Result.OdataNextLink = result.Result.OdataNextLink.Replace(oldtoken, accesstoken);
//                            refreshtokencount = refreshtokencount + 50000;
//                        }
//                        GetOrderDataprd(result.Result.OdataNextLink);  // uncomment
//                    }
//                }
//                else
//                {
//                    inventory = inventory.Concat(result2.Result.value).ToList();
//                    inventorydata = inventory;
//                    if (true)
//                    {
//                        #region ADDING DATA TO DATABASE
//                        string json = Newtonsoft.Json.JsonConvert.SerializeObject(inventory);

//                        foreach (var i in inventory)
//                        {
//                            var row = dt.NewRow();
//                            row["ID"] = i.ID;
//                            row["EAN"] = i.EAN;
//                            row["Sku"] = i.Sku;
//                            row["Title"] = i.Title;
//                            row["WarehouseLocation"] = i.WarehouseLocation;
//                            row["ProductType"] = i.ProductType;
//                            row["ParentSku"] = i.ParentSku;
//                            row["RetailPrice"] = i.RetailPrice;
//                            //row["ProfileId"] = "73000847"; //HSB
//                            row["ProfileId"] = "73000354";
//                            if (i.Attributes.Any())
//                            {
//                                foreach (var att in i.Attributes)
//                                {
//                                    if (att.Name == "Packaging Type")
//                                    {
//                                        row["PackageType"] = att.Value;
//                                    }
//                                    if (att.Name == "BC-EAN(SHOP)")
//                                    {
//                                        row["AttEAN"] = att.Value;
//                                    }
//                                }
//                            }
//                            dt.Rows.Add(row);
//                        }
//                        totalCount = totalCount + inventory.Count;
//                        #endregion
//                        if (inventory.Count >= refreshtokencount)
//                        {
//                            string oldtoken = accesstoken;
//                            Accesstoken accesstokens = GetAccessToken();
//                            accesstoken = accesstokens.access_token;
//                            result.Result.OdataNextLink = result.Result.OdataNextLink.Replace(oldtoken, accesstoken);
//                            refreshtokencount = refreshtokencount + 50000;
//                        }

//                    }
//                }
//            }

//        }
//        else
//        {
//            SendMail("Status code was not success");
//        }
//    }
//    return res;
//}
#endregion