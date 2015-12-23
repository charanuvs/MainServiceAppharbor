using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Text;
using MySql.Data;
using MySql.Data.MySqlClient;
using System.Security.Cryptography;
using System.Configuration;
using System.Threading.Tasks;
using Stripe;
using System.Web.Script.Serialization;

namespace WCFServiceWebRole1
{
    // NOTE: You can use the "Rename" command on the "Refactor" menu to change the class name "Service1" in code, svc and config file together.
    // NOTE: In order to launch WCF Test Client for testing this service, please select Service1.svc or Service1.svc.cs at the Solution Explorer and start debugging.
    public class Service1 : IService1
    {
        
        private string server;
        private string database;
        private string uid;
        private string password;
        private MySql.Data.MySqlClient.MySqlConnection connection;

        // shared private key. Not implemented.
        private string encCypher = "zH5WN0BSq1YdwtFYCk8QR5r6";

        // Start database connectivity methods
        private void initilize()
        {
            server = "bravodbinstance.c7nqnuxewgdw.us-west-2.rds.amazonaws.com";
            database = "demo";
            uid = "root";
            password = "charan92";
            string connectionString;
            connectionString = "Server = " + server + "; Database = " + database + "; Uid = " + uid + "; Pwd = " + password;

            connection = new MySql.Data.MySqlClient.MySqlConnection(connectionString);
        }
        private bool OpenConnection()
        {
            try
            {
                connection.Open();
                return true;
            }
            catch (MySql.Data.MySqlClient.MySqlException ex)
            {
                return false;
            }
        }
        private bool CloseConnection()
        {
            try
            {
                connection.Close();
                return true;
            }
            catch (MySqlException ex)
            {
                return false;
            }
        }
        // End database connectivity methods

        // Constructor iinitializes database connection
        public Service1()
        {
            initilize();
        }

        // Decryption method for critical information. Not implemented
        private string decrypt(string cipherString, bool useHashing)
        {
            byte[] keyArray;
            //get the byte code of the string

            byte[] toEncryptArray = Convert.FromBase64String(cipherString);

            System.Configuration.AppSettingsReader settingsReader = new AppSettingsReader();
            //Get your key from config file to open the lock!
            string key = encCypher;

            if (useHashing)
            {
                //if hashing was used get the hash code with regards to your key
                MD5CryptoServiceProvider hashmd5 = new MD5CryptoServiceProvider();
                keyArray = hashmd5.ComputeHash(UTF8Encoding.UTF8.GetBytes(key));
                //release any resource held by the MD5CryptoServiceProvider

                hashmd5.Clear();
            }
            else
            {
                //if hashing was not implemented get the byte code of the key
                keyArray = UTF8Encoding.UTF8.GetBytes(key);
            }

            TripleDESCryptoServiceProvider tdes = new TripleDESCryptoServiceProvider();
            //set the secret key for the tripleDES algorithm
            tdes.Key = keyArray;
            //mode of operation. there are other 4 modes.
            //We choose ECB(Electronic code Book)

            tdes.Mode = CipherMode.ECB;
            //padding mode(if any extra byte added)
            tdes.Padding = PaddingMode.PKCS7;

            ICryptoTransform cTransform = tdes.CreateDecryptor();
            byte[] resultArray = cTransform.TransformFinalBlock
                    (toEncryptArray, 0, toEncryptArray.Length);
            //Release resources held by TripleDes Encryptor
            tdes.Clear();
            //return the Clear decrypted TEXT
            return UTF8Encoding.UTF8.GetString(resultArray);
        
    }

        // Get the full name of current user from the database
        public string GetName(string id)
        {
            string name = string.Empty;
            string query = "SELECT fullname FROM USERDETAILS WHERE id = " + "'" + id + "'";
            if (this.OpenConnection() == true)
            {
                //create command and assign the query and connection from the constructor
                MySqlCommand cmd = new MySqlCommand(query, connection);

                //Execute command
                MySqlDataReader dataReader = cmd.ExecuteReader();
                if (dataReader.Read())
                {
                    name = dataReader["fullname"].ToString();
                }
                this.CloseConnection();
            }
            
            return name;
        }

        // Enter a row into the TRANSACTIONS table
        public void TransactionEntry(string tid, string uid, int amount, string state, string notes, string date)
        {
            string query = "INSERT INTO TRANSACTIONS VALUES ('"+tid+"', '"+uid+"','"+amount+"','"+state+"','"+notes+"','"+DateTime.UtcNow.ToString()+"')";
            if (this.OpenConnection() == true)
            {
                //create command and assign the query and connection from the constructor
                MySqlCommand cmd = new MySqlCommand(query, connection);

                //Execute command
                cmd.ExecuteNonQuery();

                //close connection
                this.CloseConnection();
            }

        }

        public CompositeType GetDataUsingDataContract(CompositeType composite)
        {
            throw new NotImplementedException();
        }

        // API endoint that processes the payment request. Begin here.
        public async Task<string> ProcessToken(string id, string token, string amt)
        {
            int sum = Int32.Parse(amt);
            string tid = createNewTransactionID();
            
            try {  
                string stripeCustomerId = string.Empty;
                AppSettingsReader reader = new AppSettingsReader();
                var customerService = new StripeCustomerService(reader.GetValue("StripeApiKey", typeof(string)).ToString());
                string customerID = tokenExists(id);

                // If the customer already has a customerID with Stripe, the payment source is updated with new payment source.
                // If the customer does not have a customerID with Stripe, a new customerID is created and saved.
                if (!customerID.Equals(string.Empty))
                {
                    var myCustomer = new StripeCustomerUpdateOptions();
                    myCustomer.Source = new StripeSourceOptions
                    {
                        TokenId = token
                    };
                    StripeCustomer stripeCustomer = customerService.Update(customerID, myCustomer);
                    stripeCustomerId = stripeCustomer.Id;
                    
                }
                else
                {
                    var myCustomer = new StripeCustomerCreateOptions();
                    myCustomer.Email = id;
                    myCustomer.Source = new StripeSourceOptions
                    {
                        TokenId = token
                    };
                    StripeCustomer stripeCustomer = customerService.Create(myCustomer);
                    updateCustomer(id, stripeCustomer.Id);
                    stripeCustomerId = stripeCustomer.Id;
                }

                // This method charges the customer for (sum)USD
                string res = await ChargeCustomer(stripeCustomerId, sum);
                // Enter a row iin TRANSACTIONS table
                TransactionEntry(tid, id, sum, res, "", DateTime.UtcNow.ToString());
                Response[] response = new Response[]
                {
                    new Response()
                    {
                        message = res,
                        transactionID = tid
                    }
                };
                // return response in JSON format
                return new JavaScriptSerializer().Serialize(response);
            }
            catch(Exception ex)
            {
                // If anything goes wrong, a row is added to TRANSACTIONS table with Failed state
                // Improvement: exact error details can be inserted into the table by parsing ex.Message
                TransactionEntry(tid, id, sum, "Failed", "", DateTime.UtcNow.ToString());
                Response[] response = new Response[]
                {
                    new Response()
                    {
                        message = "failed",
                        transactionID = tid
                    }
                };
                return new JavaScriptSerializer().Serialize(response);
                
            }
        }

        // creates random numeric tid
        private string createNewTransactionID()
        {
            const string chars = "0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 10)
              .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        // Updates the user in USERDETAILS table with id by updating the value of token
        private void updateCustomer(string id, string token)
        {
            string query = "UPDATE USERDETAILS SET token = '" + token + "' WHERE id = '" + id + "'";
            if (this.OpenConnection() == true)
            {
                //create mysql command
                MySqlCommand cmd = new MySqlCommand();
                //Assign the query using CommandText
                cmd.CommandText = query;
                //Assign the connection using Connection
                cmd.Connection = connection;

                //Execute query
                cmd.ExecuteNonQuery();

                //close connection
                this.CloseConnection();
            }
            
        }

        // Checks if the current user already has a customerID with Stripe
        private string tokenExists(string id)
        {

            string token = string.Empty;
            string query = "SELECT token FROM USERDETAILS WHERE id = " + "'" + id + "'";
            if (this.OpenConnection() == true)
            {
                //create command and assign the query and connection from the constructor
                MySqlCommand cmd = new MySqlCommand(query, connection);

                //Execute command
                MySqlDataReader dataReader = cmd.ExecuteReader();
                if (dataReader.Read())
                {
                    token = dataReader["token"].ToString();
                }
            }
            this.CloseConnection();
            return token;
        }

        // Charges the customer with Id = customerID with (sum) USD
        private static async Task<string> ChargeCustomer(string customerID, int sum)
        {
            return await System.Threading.Tasks.Task.Run(() =>
            {
                var myCharge = new StripeChargeCreateOptions
                {
                    Amount = sum*100,
                    Currency = "usd",
                    Description = "Lunch Credit charges.",
                    
                };

                myCharge.CustomerId = customerID;
                myCharge.Capture = true;
                AppSettingsReader reader = new AppSettingsReader();
                var chargeService = new StripeChargeService(reader.GetValue("StripeApiKey", typeof(string)).ToString());

                StripeCharge stripeCharge = chargeService.Create(myCharge);

                return stripeCharge.Status;
                //var stripeCharge = chargeService.Create(myCharge);
                //return stripeCharge.Id;
            });
        }


    }
}
