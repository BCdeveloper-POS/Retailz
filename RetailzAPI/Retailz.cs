using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using RetailzAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace RetailzAPI
{
    class Retailz
    {
        string DeveloperId = ConfigurationManager.AppSettings["DeveloperId"];
        string DiffQty = ConfigurationManager.AppSettings["differentQty"];
        //string DiffStores = ConfigurationManager.AppSettings["differentstores"];
        string PriceQuantity = ConfigurationManager.AppSettings["PriceQuantity"];
        string StaticQuantity = ConfigurationManager.AppSettings["StaticQuantity"];
        string excludeCategories = ConfigurationManager.AppSettings["ExcludeCategories"];
        // NEW - DB Config for the store being processed (StaticQty/IsNegativeToPostiveQty/Deposits/IsDepositByPack/InStockOnly/IsRoundUp)
        Config config = new Config();
        public Retailz(int storeid, decimal tax, string baseurl, string authkey, string token, Config config = null)
        {
            // NEW - Keep DB Config so ResposeToCSV can apply StaticQty/IsNegativeToPostiveQty/Deposits/IsDepositByPack/InStockOnly/IsRoundUp
            this.config = config ?? new Config();
            Console.WriteLine("Generating Product File For Store: " + storeid);
            ResposeToCSV(storeid, tax, baseurl, authkey, token);
        }

        public List<JArray> GetResponse(string baseurl, string authkey, string token, int storeid)
        {
            List<JArray> itemList = new List<JArray>();
            try
            {

                var client1 = new RestClient(baseurl + "/Item/?page=1&Size=500");
                var request1 = new RestRequest(Method.GET);
                //Console.WriteLine(client1.BuildUri(request1));
                request1.AddHeader("AuthKey", authkey);
                request1.AddHeader("Token", token);

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                IRestResponse response = client1.Execute(request1);

                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    string responseContent = response.Content;
                    var pJson = JObject.Parse(responseContent);

                    // Assuming the API returns total item counts in "totalCounts"
                    var totalCountsToken = pJson["data"]?["totalCounts"];
                    int totalCounts = totalCountsToken != null ? (int)totalCountsToken : 0;

                    // Assuming each page returns 500 items
                    int itemsPerPage = 500;
                    int totalPages = (int)Math.Ceiling((double)totalCounts / itemsPerPage);

                    // Collect every product across all pages to save one combined file
                    JArray allProducts = new JArray();

                    for (int page = 1; page <= totalPages; page++)
                    {
                        var client = new RestClient(baseurl + "/Item/?page=" + page + "&Size=" + itemsPerPage);
                        var request = new RestRequest(Method.GET);

                        request.AddHeader("AuthKey", authkey);
                        request.AddHeader("Token", token);

                        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                        IRestResponse pageResponse = client.Execute(request);

                        if (pageResponse.StatusCode == System.Net.HttpStatusCode.OK)
                        {
                            string pageResponseContent = pageResponse.Content;

                            var pageJson = JObject.Parse(pageResponseContent);
                            var dataToken = pageJson["data"];
                            var data = dataToken["data"];

                            if (data is JArray jArray)
                            {
                                itemList.Add(jArray);

                                // Add each product from this page to the combined list
                                foreach (var product in jArray)
                                {
                                    allProducts.Add(product);
                                }
                            }
                        }

                    }

                    // Complete response: all products in one valid JSON array file
                    File.WriteAllText(storeid + "Full_API_Response.json", allProducts.ToString(Newtonsoft.Json.Formatting.Indented));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("" + ex.Message);
                (new clsEmail()).sendEmail(DeveloperId, "", "", "Error in RetailzAPI@" + storeid + DateTime.UtcNow + " GMT", ex.Message + "<br/>" + ex.StackTrace);
            }
            return itemList;
        }
        public void ResposeToCSV(int storeid, decimal tax, string baseurl, string authkey, string token)
        {
            var productlist = GetResponse(baseurl, authkey, token, storeid);
            string BaseUrl = ConfigurationManager.AppSettings.Get("BaseDirectory");
            List<ProductModels> pf = new List<ProductModels>();
            List<FullName> fullname = new List<FullName>();

            ProductModels pdf;

            FullName fnf;
            try
            {
                foreach (var item in productlist)
                {
                    foreach (var itm in item)
                    {
                        pdf = new ProductModels();
                        fnf = new FullName();
                        pdf.StoreID = storeid;
                        pdf.sku = "#" + itm["sku"].ToString();
                        fnf.sku = "#" + itm["sku"].ToString();
                        pdf.upc = "#" + itm["item_Upc"].ToString();
                        fnf.upc = "#" + itm["item_Upc"].ToString();
                        pdf.uom = itm["sizeName"].ToString();
                        fnf.uom = itm["sizeName"].ToString();
                        // pdf.Tax = tax;
                        pdf.Tax = itm["tax"] != null && itm["tax"].Any() ? Convert.ToDecimal(itm["tax"].First()["persentage"]) / 100 : tax;
                        pdf.StoreProductName = itm["name"].ToString();
                        fnf.pname = itm["name"].ToString();
                        pdf.StoreDescription = itm["name"].ToString();
                        fnf.pdesc = itm["name"].ToString();
                        pdf.Start = "";
                        pdf.sprice = 0;
                        if (StaticQuantity.Contains(storeid.ToString()))
                        {
                            pdf.Qty = 999;
                        }
                        else
                        {
                            pdf.Qty = Convert.ToInt32(itm["storeQty"]) > 0 ? Convert.ToInt32(itm["storeQty"]) : 0;
                        }

                        // NEW - config: convert negative store qty to positive when configured
                        if (config.IsNegativeToPostiveQty)
                        {
                            int rawQty = Convert.ToInt32(itm["storeQty"]);
                            if (rawQty < 0)
                            {
                                pdf.Qty = Math.Abs(rawQty);
                            }
                        }
                        // NEW - config: static quantity override (final)
                        if (config.StaticQty > 0)
                        {
                            pdf.Qty = config.StaticQty;
                        }

                        pdf.Price = Convert.ToDecimal(itm["priceperUnit"]);
                        fnf.Price = Convert.ToDecimal(itm["priceperUnit"]);

                        // NEW - config: round price up to .49/.99 when configured
                        if (config.IsRoundUp && pdf.Price > 0)
                        {
                            decimal whole = Math.Floor(pdf.Price);
                            decimal cents = pdf.Price - whole;
                            pdf.Price = cents <= 0.49M ? whole + 0.49M : whole + 0.99M;
                            fnf.Price = pdf.Price;
                        }

                        pdf.pack = 1;
                        fnf.pack = 1;
                        pdf.End = "";
                        pdf.deposit = "";

                        // NEW - config: deposit override (uses pack when IsDepositByPack)
                        if (config.Deposits > 0)
                        {
                            decimal dep = config.IsDepositByPack ? config.Deposits * pdf.pack : config.Deposits;
                            pdf.deposit = dep.ToString();
                        }
                        pdf.altupc5 = "";
                        pdf.altupc4 = "";
                        pdf.altupc3 = "";
                        pdf.altupc2 = "";
                        pdf.altupc1 = "";

                        #region new include for altupcs

                        var allUpcToken = itm["allUpc"];

                        if (allUpcToken != null && allUpcToken.Type == JTokenType.Array)
                        {
                            var allUpcs = allUpcToken
                                            .Select(x => x.ToString())
                                            .Distinct()
                                            .ToList();


                            for (int i = 1; i < allUpcs.Count && i <= 5; i++)
                            {
                                string altUpcValue = "#" + allUpcs[i];

                                switch (i)
                                {
                                    case 1:
                                        pdf.altupc1 = altUpcValue;
                                        break;
                                    case 2:
                                        pdf.altupc2 = altUpcValue;
                                        break;
                                    case 3:
                                        pdf.altupc3 = altUpcValue;
                                        break;
                                    case 4:
                                        pdf.altupc4 = altUpcValue;
                                        break;
                                    case 5:
                                        pdf.altupc5 = altUpcValue;
                                        break;
                                }
                            }
                        }

                        #endregion
                        fnf.region = "";
                        fnf.pcat2 = "";
                        fnf.region = "";
                        fnf.country = "";

                        if (excludeCategories.Contains(storeid.ToString()))
                        {
                            string cat = itm["departmentName"].ToString().ToUpper();

                            if (cat.Contains("TOBACCO") ||
                                cat.Contains("E-CIGAR") ||
                                cat.Contains("CIGARETTE") ||
                                cat.Contains("CIG CTN") ||
                                cat.Contains("CIG PACK"))
                            {

                                continue;
                            }

                            else
                            {
                                fnf.pcat = itm["departmentName"].ToString();
                            }

                        }
                        else
                        {
                            fnf.pcat = itm["departmentName"].ToString();
                        }



                        // NEW - config: InStockOnly skips out-of-stock items (final qty)
                        if (config.InStockOnly && pdf.Qty <= 0)
                        {
                            continue;
                        }

                        if (PriceQuantity.Contains(storeid.ToString()))
                        {
                            if (pdf.Qty > 0 && pdf.Price > 0)
                            {
                                pf.Add(pdf);
                                fullname.Add(fnf);
                            }
                        }
                        else
                        {
                            if (pdf.Price > 0)
                            {
                                pf.Add(pdf);
                                fullname.Add(fnf);
                            }
                        }
                    }
                }

                if (storeid == 12007)
                {
                    authkey = "2dT0WBgbU+WHYUoo0ZhqbIMo4oEds3PwrxrwITMgmLw=";
                    token = "o+bMoXeNz10DCdSA+HIJLUSu8vR8t2bQsvcEUgFsTvXJUJ0APwCcboDj0MOVlFRQJc+6ffaR4lB1xgAnQvSO4Bw9mK1GEFzQ7wuNI2M9mtuNX+4CY9QQUX8RnD5KpEj19z5VAEKzqXbGqWLp2gREmkZm/8Ea6qxtsz4OXAfOFCw=";
                    var OtherStoreproductlist = GetResponse(baseurl, authkey, token, storeid);

                    List<ProductModels> pf1 = new List<ProductModels>();
                    List<FullName> fullname1 = new List<FullName>();

                    foreach (var items in OtherStoreproductlist)
                    {
                        foreach (var itms in items)
                        {
                            pdf = new ProductModels();
                            fnf = new FullName();

                            pdf.StoreID = storeid;
                            pdf.sku = "#" + itms["sku"].ToString();
                            fnf.sku = "#" + itms["sku"].ToString();
                            pdf.upc = "#" + itms["item_Upc"].ToString();
                            fnf.upc = "#" + itms["item_Upc"].ToString();
                            pdf.uom = itms["sizeName"].ToString();
                            fnf.uom = itms["sizeName"].ToString();
                            pdf.Tax = tax;
                            pdf.StoreProductName = itms["name"].ToString();
                            fnf.pname = itms["name"].ToString();
                            pdf.StoreDescription = itms["name"].ToString();
                            fnf.pdesc = itms["name"].ToString();
                            pdf.Start = "";
                            pdf.sprice = 0;
                            pdf.Qty = Convert.ToInt32(itms["storeQty"]) > 0 ? Convert.ToInt32(itms["storeQty"]) : 0;

                            // NEW - config: convert negative store qty to positive when configured
                            if (config.IsNegativeToPostiveQty)
                            {
                                int rawQty = Convert.ToInt32(itms["storeQty"]);
                                if (rawQty < 0)
                                {
                                    pdf.Qty = Math.Abs(rawQty);
                                }
                            }
                            // NEW - config: static quantity override (final)
                            if (config.StaticQty > 0)
                            {
                                pdf.Qty = config.StaticQty;
                            }

                            pdf.Price = Convert.ToDecimal(itms["priceperUnit"]);
                            fnf.Price = Convert.ToDecimal(itms["priceperUnit"]);

                            // NEW - config: round price up to .49/.99 when configured
                            if (config.IsRoundUp && pdf.Price > 0)
                            {
                                decimal whole = Math.Floor(pdf.Price);
                                decimal cents = pdf.Price - whole;
                                pdf.Price = cents <= 0.49M ? whole + 0.49M : whole + 0.99M;
                                fnf.Price = pdf.Price;
                            }

                            pdf.pack = 1;
                            fnf.pack = 1;
                            pdf.End = "";
                            pdf.deposit = "";

                            // NEW - config: deposit override (uses pack when IsDepositByPack)
                            if (config.Deposits > 0)
                            {
                                decimal dep = config.IsDepositByPack ? config.Deposits * pdf.pack : config.Deposits;
                                pdf.deposit = dep.ToString();
                            }

                            pdf.altupc5 = "";
                            pdf.altupc4 = "";
                            pdf.altupc3 = "";
                            pdf.altupc2 = "";
                            pdf.altupc1 = "";
                            fnf.region = "";
                            fnf.pcat2 = "";
                            fnf.region = "";
                            fnf.country = "";
                            fnf.pcat = itms["departmentName"].ToString();

                            // NEW - config: InStockOnly skips out-of-stock items (final qty)
                            if (config.InStockOnly && pdf.Qty <= 0)
                            {
                                continue;
                            }

                            if (DiffQty.Contains(storeid.ToString()))
                            {
                                if (pdf.Qty > 0)
                                {
                                    pf.Add(pdf);
                                    fullname.Add(fnf);

                                }
                            }
                            else if (pdf.Price > 0)
                            {
                                pf.Add(pdf);
                                fullname.Add(fnf);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine("" + ex.Message);
                (new clsEmail()).sendEmail(DeveloperId, "", "", "Error in RetailzAPI@" + storeid + DateTime.UtcNow + " GMT", ex.Message + "<br/>" + ex.StackTrace);
            }
            pf = pf.GroupBy(p => p.sku)
                                     .Select(g => g.First())
                                     .ToList();
            fullname = fullname.GroupBy(p => p.sku)
                                     .Select(g => g.First())
                                     .ToList();


            GenerateCSV.GenerateCSVFile(pf, "PRODUCT", storeid, BaseUrl);
            GenerateCSV.GenerateCSVFile(fullname, "FullName", storeid, BaseUrl);
            Console.WriteLine("Product file generated for RetailZAPI " + storeid);
            Console.WriteLine("FullName file generated for RetailZAPI " + storeid);
        }
    }
}


/*using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using RetailzAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace RetailzAPI
{
    class Retailz
    {
        string DeveloperId = ConfigurationManager.AppSettings["DeveloperId"];
        string DiffQty = ConfigurationManager.AppSettings["differentQty"];
        //string DiffStores = ConfigurationManager.AppSettings["differentstores"];
        string PriceQuantity = ConfigurationManager.AppSettings["PriceQuantity"];
        string StaticQuantity = ConfigurationManager.AppSettings["StaticQuantity"];
        string excludeCategories = ConfigurationManager.AppSettings["ExcludeCategories"] ;
        public Retailz(int storeid, decimal tax, string baseurl, string authkey, string token)
        {
            Console.WriteLine("Generating Product File For Store: "+storeid);
            ResposeToCSV(storeid, tax, baseurl, authkey, token);
        }

        public List<JArray> GetResponse(string baseurl, string authkey, string token, int storeid)
        {
            List<JArray> itemList = new List<JArray>();
            try
            {

                var client1 = new RestClient(baseurl + "/Item/?page=1&Size=500");
                var request1 = new RestRequest(Method.GET);
                //Console.WriteLine(client1.BuildUri(request1));
                request1.AddHeader("AuthKey", authkey);
                request1.AddHeader("Token", token);

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                IRestResponse response = client1.Execute(request1);

                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    string responseContent = response.Content;
                    var pJson = JObject.Parse(responseContent);

                    // Assuming the API returns total item counts in "totalCounts"
                    var totalCountsToken = pJson["data"]?["totalCounts"];
                    int totalCounts = totalCountsToken != null ? (int)totalCountsToken : 0;

                    // Assuming each page returns 500 items
                    int itemsPerPage = 500;
                    int totalPages = (int)Math.Ceiling((double)totalCounts / itemsPerPage);

                    for (int page = 1; page <= totalPages; page++)
                    {
                        var client = new RestClient(baseurl + "/Item/?page=" + page +"&Size="+itemsPerPage);
                        var request = new RestRequest(Method.GET);

                        request.AddHeader("AuthKey", authkey);
                        request.AddHeader("Token", token);

                        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                        IRestResponse pageResponse = client.Execute(request);

                        if (pageResponse.StatusCode == System.Net.HttpStatusCode.OK)
                        {
                            string pageResponseContent = pageResponse.Content;
                            //File.AppendAllText("12007.json", pageResponseContent);

                            var pageJson = JObject.Parse(pageResponseContent);
                            var dataToken = pageJson["data"];
                            var data = dataToken["data"];

                            if (data is JArray jArray)
                            {
                                itemList.Add(jArray);
                            }
                        }

                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("" + ex.Message);
                (new clsEmail()).sendEmail(DeveloperId, "", "", "Error in RetailzAPI@" + storeid + DateTime.UtcNow + " GMT", ex.Message + "<br/>" + ex.StackTrace);
            }
            return itemList;
        }
        public void ResposeToCSV(int storeid, decimal tax, string baseurl, string authkey, string token)
        {
            var productlist = GetResponse(baseurl, authkey, token, storeid);
            string BaseUrl = ConfigurationManager.AppSettings.Get("BaseDirectory");
            List<ProductModels> pf = new List<ProductModels>();
            List<FullName> fullname = new List<FullName>();

            ProductModels pdf;

            FullName fnf;
            try
            {
                foreach (var item in productlist)
                {
                    foreach (var itm in item)
                    {
                        pdf = new ProductModels();
                        fnf = new FullName();
                        pdf.StoreID = storeid;
                        pdf.sku = "#" + itm["sku"].ToString();
                        fnf.sku = "#" + itm["sku"].ToString();
                        pdf.upc = "#" + itm["item_Upc"].ToString();
                        fnf.upc = "#" + itm["item_Upc"].ToString();
                        pdf.uom = itm["sizeName"].ToString();
                        fnf.uom = itm["sizeName"].ToString();
                        // pdf.Tax = tax;
                        pdf.Tax = itm["tax"] != null && itm["tax"].Any() ? Convert.ToDecimal(itm["tax"].First()["persentage"]) / 100 : tax;
                        pdf.StoreProductName = itm["name"].ToString();
                        fnf.pname = itm["name"].ToString();
                        pdf.StoreDescription = itm["name"].ToString();
                        fnf.pdesc = itm["name"].ToString();
                        pdf.Start = "";
                        pdf.sprice = 0;
                        if (StaticQuantity.Contains(storeid.ToString()))
                        {
                            pdf.Qty = 999;
                        }
                        else
                        {
                            pdf.Qty = Convert.ToInt32(itm["storeQty"]) > 0 ? Convert.ToInt32(itm["storeQty"]) : 0;
                        }

                        pdf.Price = Convert.ToDecimal(itm["priceperUnit"]);
                        fnf.Price = Convert.ToDecimal(itm["priceperUnit"]);
                        pdf.pack = 1;
                        fnf.pack = 1;
                        pdf.End = "";
                        pdf.deposit = "";
                        pdf.altupc5 = "";
                        pdf.altupc4 = "";
                        pdf.altupc3 = "";
                        pdf.altupc2 = "";
                        pdf.altupc1 = "";

                        #region new include for altupcs
                        
                        var allUpcToken = itm["allUpc"];

                        if (allUpcToken != null && allUpcToken.Type == JTokenType.Array)
                        {
                            var allUpcs = allUpcToken
                                            .Select(x => x.ToString())
                                            .Distinct()
                                            .ToList();

                            
                            for (int i = 1; i < allUpcs.Count && i <= 5; i++)
                            {
                                string altUpcValue = "#" + allUpcs[i];

                                switch (i)
                                {
                                    case 1:
                                        pdf.altupc1 = altUpcValue;
                                        break;
                                    case 2:
                                        pdf.altupc2 = altUpcValue;
                                        break;
                                    case 3:
                                        pdf.altupc3 = altUpcValue;
                                        break;
                                    case 4:
                                        pdf.altupc4 = altUpcValue;
                                        break;
                                    case 5:
                                        pdf.altupc5 = altUpcValue;
                                        break;
                                }
                            }
                        }

                        #endregion
                        fnf.region = "";
                        fnf.pcat2 = "";
                        fnf.region = "";
                        fnf.country = "";

                        if (excludeCategories.Contains(storeid.ToString()))
                        {
                            string cat = itm["departmentName"].ToString().ToUpper();

                            if (cat.Contains("TOBACCO") ||
                                cat.Contains("E-CIGAR") ||
                                cat.Contains("CIGARETTE") ||
                                cat.Contains("CIG CTN") ||
                                cat.Contains("CIG PACK"))
                            {

                                continue;
                            }

                            else
                        {
                            fnf.pcat = itm["departmentName"].ToString();
                        }

                        }
                        else
                        {
                            fnf.pcat = itm["departmentName"].ToString();
                        }



                        if (PriceQuantity.Contains(storeid.ToString()))
                        {
                            if (pdf.Qty > 0 && pdf.Price > 0)
                            {
                                pf.Add(pdf);
                                fullname.Add(fnf);
                            }
                        }
                        else
                        {
                            if (pdf.Price > 0)
                            {
                                pf.Add(pdf);
                                fullname.Add(fnf);
                            }
                        }                      
                    }
                }

                if (storeid == 12007)
                {
                    authkey = "2dT0WBgbU+WHYUoo0ZhqbIMo4oEds3PwrxrwITMgmLw=";
                    token = "o+bMoXeNz10DCdSA+HIJLUSu8vR8t2bQsvcEUgFsTvXJUJ0APwCcboDj0MOVlFRQJc+6ffaR4lB1xgAnQvSO4Bw9mK1GEFzQ7wuNI2M9mtuNX+4CY9QQUX8RnD5KpEj19z5VAEKzqXbGqWLp2gREmkZm/8Ea6qxtsz4OXAfOFCw=";
                    var OtherStoreproductlist = GetResponse(baseurl, authkey, token, storeid);

                    List<ProductModels> pf1 = new List<ProductModels>();
                    List<FullName> fullname1 = new List<FullName>();

                    foreach (var items in OtherStoreproductlist)
                    {
                        foreach (var itms in items)
                        {
                            pdf = new ProductModels();
                            fnf = new FullName();

                            pdf.StoreID = storeid;
                            pdf.sku = "#" + itms["sku"].ToString();
                            fnf.sku = "#" + itms["sku"].ToString();
                            pdf.upc = "#" + itms["item_Upc"].ToString();
                            fnf.upc = "#" + itms["item_Upc"].ToString();
                            pdf.uom = itms["sizeName"].ToString();
                            fnf.uom = itms["sizeName"].ToString();
                            pdf.Tax = tax;
                            pdf.StoreProductName = itms["name"].ToString();
                            fnf.pname = itms["name"].ToString();
                            pdf.StoreDescription = itms["name"].ToString();
                            fnf.pdesc = itms["name"].ToString();
                            pdf.Start = "";
                            pdf.sprice = 0;
                            pdf.Qty = Convert.ToInt32(itms["storeQty"]) > 0 ? Convert.ToInt32(itms["storeQty"]) : 0;
                            pdf.Price = Convert.ToDecimal(itms["priceperUnit"]);
                            fnf.Price = Convert.ToDecimal(itms["priceperUnit"]);
                            pdf.pack = 1;
                            fnf.pack = 1;
                            pdf.End = "";
                            pdf.deposit = "";
                            pdf.altupc5 = "";
                            pdf.altupc4 = "";
                            pdf.altupc3 = "";
                            pdf.altupc2 = "";
                            pdf.altupc1 = "";
                            fnf.region = "";
                            fnf.pcat2 = "";
                            fnf.region = "";
                            fnf.country = "";
                            fnf.pcat = itms["departmentName"].ToString();


                            if (DiffQty.Contains(storeid.ToString()))
                            {
                                if (pdf.Qty > 0)
                                {
                                    pf.Add(pdf);
                                    fullname.Add(fnf);

                                }
                            }
                            else if (pdf.Price > 0)
                            {
                                pf.Add(pdf);
                                fullname.Add(fnf);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine("" + ex.Message);
                (new clsEmail()).sendEmail(DeveloperId, "", "", "Error in RetailzAPI@" + storeid + DateTime.UtcNow + " GMT", ex.Message + "<br/>" + ex.StackTrace);
            }
            pf = pf.GroupBy(p => p.sku)
                                     .Select(g => g.First())
                                     .ToList();
            fullname = fullname.GroupBy(p => p.sku)
                                     .Select(g => g.First())
                                     .ToList();


            GenerateCSV.GenerateCSVFile(pf, "PRODUCT", storeid, BaseUrl);
            GenerateCSV.GenerateCSVFile(fullname, "FullName", storeid, BaseUrl);
            Console.WriteLine("Product file generated for RetailZAPI " + storeid);
            Console.WriteLine("FullName file generated for RetailZAPI " + storeid);
        }
    }
}
*/