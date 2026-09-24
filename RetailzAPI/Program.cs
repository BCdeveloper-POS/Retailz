using RetailzAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RetailzAPI
{
    class Program
    {
        static void Main(string[] args)
        {
            string DeveloperId = ConfigurationManager.AppSettings["DeveloperId"];
            try
            {
                POSsettings pOSSettings = new POSsettings();
                pOSSettings.IntializeStoreSettings();
                foreach (POSSetting current in pOSSettings.PosDetails)
                {
                    try
                    {
                        //if (current.StoreSettings.StoreId == 12374 )
                        //{
                        //    Console.WriteLine("Fetching_storeid"+ current.StoreSettings.StoreId);
                        //}
                        //else
                        //{
                        //    continue; 
                        //}

                        if (current.PosName.ToUpper() == "RETAILZPOS")
                        {
                            //   Retailz retailz = new Retailz(current.StoreSettings.StoreId, current.StoreSettings.POSSettings.tax, current.StoreSettings.POSSettings.BaseUrl, current.StoreSettings.POSSettings.AuthKey, current.StoreSettings.POSSettings.Token);
                            Retailz retailz = new Retailz(current.StoreSettings.StoreId, current.StoreSettings.POSSettings.tax, current.StoreSettings.POSSettings.BaseUrl, current.StoreSettings.POSSettings.AuthKey, current.StoreSettings.POSSettings.Token, current.config); // NEW - pass DB Config
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            finally
            {

            }
        }
    }
}
