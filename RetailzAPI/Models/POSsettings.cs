using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;

namespace RetailzAPI.Models
{
    class POSsettings
    {
        public List<POSSetting> PosDetails { get; set; }
        public void IntializeStoreSettings()
        {
            DataSet dsResult = new DataSet();
            List<POSSetting> posdetails = new List<POSSetting>();
            //List<StoreSetting> StoreList = new List<StoreSetting>();
            try
            {
                //string constr = ConfigurationManager.AppSettings.Get("LiquorAppsConnectionString");

                List<SqlParameter> sparams = new List<SqlParameter>();
                sparams.Add(new SqlParameter("@PosId", 83));

                //Handling missing dbsettings.json File 
                string constr = ConfigurationManager.AppSettings["LiquorAppsConnectionString"];
                Console.WriteLine("constr-from-Appconfig " + constr);
                // If App.config doesn't have the connection string, use dbsettings.json
                if (string.IsNullOrWhiteSpace(constr))
                {
                    string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dbsettings.json");

                    if (File.Exists(filePath))
                    {
                        try
                        {
                            DbSettings dbcon = JsonConvert.DeserializeObject<DbSettings>(
                                File.ReadAllText(filePath));

                            if (dbcon?.liquorappsconnectionstring != null &&
                                dbcon.liquorappsconnectionstring.Count > 0)
                            {
                                // Local connection string
                                constr = dbcon.liquorappsconnectionstring[1]; // [0] is for local & [1] for live db 
                                Console.WriteLine("constr-2 " + constr);
                            }
                        }
                        catch
                        {
                            // Ignore and handle if constr is still null
                        }
                    }

                }


                using (SqlConnection con = new SqlConnection(constr))
                {
                    con.Open();

                    Console.WriteLine(con.Database);
                    Console.WriteLine(con.DataSource);

                    using (SqlCommand cmd = new SqlCommand())
                    {
                        cmd.Connection = con;
                         cmd.Parameters.Add(sparams[0]);
                        cmd.CommandText = "usp_ts_GetStorePosSetting";
                        cmd.CommandType = CommandType.StoredProcedure;
                        using (SqlDataAdapter da = new SqlDataAdapter())
                        {
                            da.SelectCommand = cmd;
                            da.Fill(dsResult);
                        }
                        Console.WriteLine("Tables: " + dsResult.Tables.Count);

                        if (dsResult.Tables.Count > 0)
                        {
                            Console.WriteLine("Rows: " + dsResult.Tables[0].Rows.Count);
                        }
                    }
                }
                if (dsResult != null || dsResult.Tables.Count > 0)
                {
                    foreach (DataRow dr in dsResult.Tables[0].Rows)
                    {
                        POSSetting pobj = new POSSetting();
                        pobj.Setting = dr["Settings"].ToString();
                        StoreSetting obj = new StoreSetting();
                        obj.StoreId = Convert.ToInt32(dr["StoreId"] == DBNull.Value ? 0 : dr["StoreId"]);
                        obj.POSSettings = JsonConvert.DeserializeObject<Setting>(pobj.Setting);
                        pobj.PosName = dr["PosName"].ToString();
                        pobj.PosId = Convert.ToInt32(dr["PosId"]);
                        pobj.StoreSettings = obj;

                        // NEW - Read DB Config column into config (StaticQty/IsNegativeToPostiveQty/Deposits/IsDepositByPack/InStockOnly/IsRoundUp)
                        if (dsResult.Tables[0].Columns.Contains("Config") && dr["Config"] != DBNull.Value && !string.IsNullOrWhiteSpace(dr["Config"].ToString()))
                        {
                            pobj.config = JsonConvert.DeserializeObject<Config>(dr["Config"].ToString());
                        }
                        if (pobj.config == null)
                        {
                            pobj.config = new Config();
                        }

                        if (pobj.StoreSettings.POSSettings != null)
                        {
                            pobj.StoreSettings.POSSettings.categoriess = obj.POSSettings.categoriess;
                            pobj.StoreSettings.POSSettings.Upc = obj.POSSettings.Upc;
                        }
                        posdetails.Add(pobj);
                    }
                }
                PosDetails = posdetails;

            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.Read();

            }

        }
    }
    public class POSSetting
    {
        public int PosId { get; set; }
        public string PosName { get; set; }
        public StoreSetting StoreSettings { get; set; }
        public string Setting { get; set; }
        // NEW - DB Config for this store
        public Config config { get; set; }
    }
    // NEW - DB Config model (values come from the DB Config tab)
    public class Config
    {
        public int StaticQty { get; set; }
        public bool IsNegativeToPostiveQty { get; set; }
        public decimal Deposits { get; set; }
        public bool IsDepositByPack { get; set; }
        public bool InStockOnly { get; set; }
        // Round price up to .49/.99 when configured
        public bool IsRoundUp { get; set; }
    }
    public class StoreSetting
    {
        public int StoreId { get; set; }
        public Setting POSSettings { get; set; }
    }
    public class Setting
    {
        public string AuthKey { get; set; }
        public decimal tax { get; set; }
        public string Token { get; set; }
        public string BaseUrl { get; set; }
        public List<categories> categoriess { set; get; }
        public List<UPC> Upc { get; set; }

    }
    public class categories
    {
        public string id { get; set; }
        public string name { get; set; }
        public decimal taxrate { get; set; }
        public Boolean selected { get; set; }
    }
    public class UPC
    {
        public string upccode { get; set; }
    }

    public class DbSettings
    {
        public List<string> liquorappsconnectionstring { get; set; }

    }
}
