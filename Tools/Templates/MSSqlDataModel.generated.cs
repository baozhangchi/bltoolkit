﻿//---------------------------------------------------------------------------------------------------
// <auto-generated>
//    This code was generated by BLToolkit template for T4.
//    Changes to this file may cause incorrect behavior and will be lost if the code is regenerated.
// </auto-generated>
//---------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;

using BLToolkit.Data;
using BLToolkit.Data.DataProvider;
using BLToolkit.Data.Linq;
using BLToolkit.Data.Sql;
using BLToolkit.Data.Sql.SqlProvider;
using BLToolkit.DataAccess;
using BLToolkit.Mapping;
using BLToolkit.Validation;

namespace Templates.MSSql
{
	public partial class MSSqlDataModel : DbManager
	{
		public Table<AlphabeticalListOfProduct>  AlphabeticalListOfProducts   { get { return this.GetTable<AlphabeticalListOfProduct>();  } }
		public Table<Category>                   Categories                   { get { return this.GetTable<Category>();                   } }
		public Table<CategorySalesFor1997>       CategorySalesFor1997         { get { return this.GetTable<CategorySalesFor1997>();       } }
		public Table<CurrentProductList>         CurrentProductLists          { get { return this.GetTable<CurrentProductList>();         } }
		public Table<CustomerAndSuppliersByCity> CustomerAndSuppliersByCities { get { return this.GetTable<CustomerAndSuppliersByCity>(); } }
		public Table<CustomerCustomerDemo>       CustomerCustomerDemos        { get { return this.GetTable<CustomerCustomerDemo>();       } }
		public Table<CustomerDemographic>        CustomerDemographics         { get { return this.GetTable<CustomerDemographic>();        } }
		public Table<Customer>                   Customers                    { get { return this.GetTable<Customer>();                   } }
		public Table<Employee>                   Employees                    { get { return this.GetTable<Employee>();                   } }
		public Table<EmployeeTerritory>          EmployeeTerritories          { get { return this.GetTable<EmployeeTerritory>();          } }
		public Table<Invoice>                    Invoices                     { get { return this.GetTable<Invoice>();                    } }
		public Table<OrderDetail>                OrderDetails                 { get { return this.GetTable<OrderDetail>();                } }
		public Table<OrderDetailsExtended>       OrderDetailsExtendeds        { get { return this.GetTable<OrderDetailsExtended>();       } }
		public Table<OrderSubtotal>              OrderSubtotals               { get { return this.GetTable<OrderSubtotal>();              } }
		public Table<Order>                      Orders                       { get { return this.GetTable<Order>();                      } }
		public Table<OrdersQry>                  OrdersQries                  { get { return this.GetTable<OrdersQry>();                  } }
		public Table<ProductSalesFor1997>        ProductSalesFor1997          { get { return this.GetTable<ProductSalesFor1997>();        } }
		public Table<Product>                    Products                     { get { return this.GetTable<Product>();                    } }
		public Table<ProductsAboveAveragePrice>  ProductsAboveAveragePrices   { get { return this.GetTable<ProductsAboveAveragePrice>();  } }
		public Table<ProductsByCategory>         ProductsByCategories         { get { return this.GetTable<ProductsByCategory>();         } }
		public Table<QuarterlyOrder>             QuarterlyOrders              { get { return this.GetTable<QuarterlyOrder>();             } }
		public Table<Region>                     Regions                      { get { return this.GetTable<Region>();                     } }
		public Table<SalesByCategory>            SalesByCategories            { get { return this.GetTable<SalesByCategory>();            } }
		public Table<SalesTotalsByAmount>        SalesTotalsByAmounts         { get { return this.GetTable<SalesTotalsByAmount>();        } }
		public Table<Shipper>                    Shippers                     { get { return this.GetTable<Shipper>();                    } }
		public Table<SummaryOfSalesByQuarter>    SummaryOfSalesByQuarters     { get { return this.GetTable<SummaryOfSalesByQuarter>();    } }
		public Table<SummaryOfSalesByYear>       SummaryOfSalesByYears        { get { return this.GetTable<SummaryOfSalesByYear>();       } }
		public Table<Supplier>                   Suppliers                    { get { return this.GetTable<Supplier>();                   } }
		public Table<Territory>                  Territories                  { get { return this.GetTable<Territory>();                  } }
		
		#region FreeTextTable
		
		public class FreeTextKey<T>
		{
			public T   Key;
			public int Rank;
		}
		
		class FreeTextTableExpressionAttribute : TableExpressionAttribute
		{
			public FreeTextTableExpressionAttribute()
				: base("")
			{
			}
		
			public override void SetTable(SqlTable table, MemberInfo member, IEnumerable<Expression> expArgs, IEnumerable<ISqlExpression> sqlArgs)
			{
				var aargs  = sqlArgs.ToArray();
				var arr    = ConvertArgs(member, aargs).ToList();
				var method = (MethodInfo)member;
				var sp     = new MsSql2008SqlProvider();
		
				{
					var ttype  = method.GetGenericArguments()[0];
					var tbl    = new SqlTable(ttype);
		
					var database     = tbl.Database     == null ? null : sp.Convert(tbl.Database,     ConvertType.NameToDatabase).  ToString();
					var owner        = tbl.Owner        == null ? null : sp.Convert(tbl.Owner,        ConvertType.NameToOwner).     ToString();
					var physicalName = tbl.PhysicalName == null ? null : sp.Convert(tbl.PhysicalName, ConvertType.NameToQueryTable).ToString();
		
					var name   = sp.BuildTableName(new StringBuilder(), database, owner, physicalName);
		
					arr.Add(new SqlExpression(name.ToString(), Precedence.Primary));
				}
		
				{
					var field = ((ConstantExpression)expArgs.First()).Value;
		
					if (field is string)
					{
						arr[0] = new SqlExpression(field.ToString(), Precedence.Primary);
					}
					else if (field is LambdaExpression)
					{
						var body = ((LambdaExpression)field).Body;
		
						if (body is MemberExpression)
						{
							var name = ((MemberExpression)body).Member.Name;
		
							name = sp.Convert(name, ConvertType.NameToQueryField).ToString();
		
							arr[0] = new SqlExpression(name, Precedence.Primary);
						}
					}
				}
		
				table.SqlTableType   = SqlTableType.Expression;
				table.Name           = "FREETEXTTABLE({6}, {2}, {3}) {1}";
				table.TableArguments = arr.ToArray();
			}
		}
		
		[FreeTextTableExpressionAttribute]
		public Table<FreeTextKey<TKey>> FreeTextTable<TTable,TKey>(string field, string text)
		{
			return GetTable<FreeTextKey<TKey>>(
				this,
				((MethodInfo)(MethodBase.GetCurrentMethod())).MakeGenericMethod(typeof(TTable), typeof(TKey)),
				field,
				text);
		}
		
		[FreeTextTableExpressionAttribute]
		public Table<FreeTextKey<TKey>> FreeTextTable<TTable,TKey>(Expression<Func<TTable,string>> fieldSelector, string text)
		{
			return GetTable<FreeTextKey<TKey>>(
				this,
				((MethodInfo)(MethodBase.GetCurrentMethod())).MakeGenericMethod(typeof(TTable), typeof(TKey)),
				fieldSelector,
				text);
		}
		
		#endregion
	}

	// View
	[Serializable, DataContract]
	[TableName(Name="Alphabetical list of products")]
	public partial class AlphabeticalListOfProduct
	{
		[MapField("ProductID"),           DataMember, Required               ] public int      MyProductID     { get; set; } // int(10)
		[                                 DataMember, MaxLength(40), Required] public string   ProductName     { get; set; } // nvarchar(40)
		[                       Nullable, DataMember                         ] public int?     SupplierID      { get; set; } // int(10)
		[                       Nullable, DataMember                         ] public int?     CategoryID      { get; set; } // int(10)
		[                       Nullable, DataMember, MaxLength(20)          ] public string   QuantityPerUnit { get; set; } // nvarchar(20)
		[                       Nullable, DataMember                         ] public decimal? UnitPrice       { get; set; } // money(19,4)
		[                       Nullable, DataMember                         ] public short?   UnitsInStock    { get; set; } // smallint(5)
		[                       Nullable, DataMember                         ] public short?   UnitsOnOrder    { get; set; } // smallint(5)
		[                       Nullable, DataMember                         ] public short?   ReorderLevel    { get; set; } // smallint(5)
		[                                 DataMember, Required               ] public bool     Discontinued    { get; set; } // bit
		[                                 DataMember, MaxLength(15), Required] public string   CategoryName    { get; set; } // nvarchar(15)
	}

	[Serializable, DataContract]
	[TableName(Name="Categories")]
	public partial class Category
	{
		[Identity, PrimaryKey(1), DataMember, Required               ] public int    CategoryID   { get; set; } // int(10)
		[                         DataMember, MaxLength(15), Required] public string CategoryName { get; set; } // nvarchar(15)
		[Nullable,                DataMember, MaxLength(1073741823)  ] public string Description  { get; set; } // ntext(1073741823)
		[Nullable,                DataMember                         ] public byte[] Picture      { get; set; } // image(2147483647)

		// FK_Products_Categories_BackReference
		[Association(ThisKey="CategoryID", OtherKey="CategoryID", CanBeNull=true)]
		public IEnumerable<Product> Products { get; set; }
	}

	// View
	[Serializable, DataContract]
	[TableName(Name="Category Sales for 1997")]
	public partial class CategorySalesFor1997
	{
		[          DataMember, MaxLength(15), Required] public string   CategoryName  { get; set; } // nvarchar(15)
		[Nullable, DataMember                         ] public decimal? CategorySales { get; set; } // money(19,4)
	}

	// View
	[Serializable, DataContract]
	[TableName(Name="Current Product List")]
	public partial class CurrentProductList
	{
		[Identity, DataMember, Required               ] public int    ProductID   { get; set; } // int(10)
		[          DataMember, MaxLength(40), Required] public string ProductName { get; set; } // nvarchar(40)
	}

	// View
	[Serializable, DataContract]
	[TableName(Name="Customer and Suppliers by City")]
	public partial class CustomerAndSuppliersByCity
	{
		[Nullable, DataMember, MaxLength(15)          ] public string City         { get; set; } // nvarchar(15)
		[          DataMember, MaxLength(40), Required] public string CompanyName  { get; set; } // nvarchar(40)
		[Nullable, DataMember, MaxLength(30)          ] public string ContactName  { get; set; } // nvarchar(30)
		[          DataMember, MaxLength(9), Required ] public string Relationship { get; set; } // varchar(9)
	}

	[Serializable, DataContract]
	[TableName(Name="CustomerCustomerDemo")]
	public partial class CustomerCustomerDemo
	{
		[PrimaryKey(1), DataMember, MaxLength(5), Required ] public string CustomerID     { get; set; } // nchar(5)
		[PrimaryKey(2), DataMember, MaxLength(10), Required] public string CustomerTypeID { get; set; } // nchar(10)

		// FK_CustomerCustomerDemo
		[Association(ThisKey="CustomerTypeID", OtherKey="CustomerTypeID", CanBeNull=false)]
		public CustomerDemographic FK_CustomerCustomerDemo { get; set; }

		// FK_CustomerCustomerDemo_Customers
		[Association(ThisKey="CustomerID", OtherKey="CustomerID", CanBeNull=false)]
		public Customer Customer { get; set; }
	}

	[Serializable, DataContract]
	[TableName(Name="CustomerDemographics")]
	public partial class CustomerDemographic
	{
		[          PrimaryKey(1), DataMember, MaxLength(10), Required] public string CustomerTypeID { get; set; } // nchar(10)
		[Nullable,                DataMember, MaxLength(1073741823)  ] public string CustomerDesc   { get; set; } // ntext(1073741823)

		// FK_CustomerCustomerDemo_BackReference
		[Association(ThisKey="CustomerTypeID", OtherKey="CustomerTypeID", CanBeNull=true)]
		public IEnumerable<CustomerCustomerDemo> CustomerCustomerDemos { get; set; }
	}

	[Serializable, DataContract]
	[TableName(Name="Customers")]
	public partial class Customer
	{
		[          PrimaryKey(1), DataMember, MaxLength(5), Required ] public string CustomerID   { get; set; } // nchar(5)
		[                         DataMember, MaxLength(40), Required] public string CompanyName  { get; set; } // nvarchar(40)
		[Nullable,                DataMember, MaxLength(30)          ] public string ContactName  { get; set; } // nvarchar(30)
		[Nullable,                DataMember, MaxLength(30)          ] public string ContactTitle { get; set; } // nvarchar(30)
		[Nullable,                DataMember, MaxLength(60)          ] public string Address      { get; set; } // nvarchar(60)
		[Nullable,                DataMember, MaxLength(15)          ] public string City         { get; set; } // nvarchar(15)
		[Nullable,                DataMember, MaxLength(15)          ] public string Region       { get; set; } // nvarchar(15)
		[Nullable,                DataMember, MaxLength(10)          ] public string PostalCode   { get; set; } // nvarchar(10)
		[Nullable,                DataMember, MaxLength(15)          ] public string Country      { get; set; } // nvarchar(15)
		[Nullable,                DataMember, MaxLength(24)          ] public string Phone        { get; set; } // nvarchar(24)
		[Nullable,                DataMember, MaxLength(24)          ] public string Fax          { get; set; } // nvarchar(24)

		// FK_Orders_Customers_BackReference
		[Association(ThisKey="CustomerID", OtherKey="CustomerID", CanBeNull=true)]
		public IEnumerable<Order> Orders { get; set; }

		// FK_CustomerCustomerDemo_Customers_BackReference
		[Association(ThisKey="CustomerID", OtherKey="CustomerID", CanBeNull=true)]
		public IEnumerable<CustomerCustomerDemo> CustomerCustomerDemos { get; set; }
	}

	[Serializable, DataContract]
	[TableName(Name="Employees")]
	public partial class Employee
	{
		[Identity, PrimaryKey(1), DataMember, Required               ] public int       EmployeeID      { get; set; } // int(10)
		[                         DataMember, MaxLength(20), Required] public string    LastName        { get; set; } // nvarchar(20)
		[                         DataMember, MaxLength(10), Required] public string    FirstName       { get; set; } // nvarchar(10)
		[Nullable,                DataMember, MaxLength(30)          ] public string    Title           { get; set; } // nvarchar(30)
		[Nullable,                DataMember, MaxLength(25)          ] public string    TitleOfCourtesy { get; set; } // nvarchar(25)
		[Nullable,                DataMember                         ] public DateTime? BirthDate       { get; set; } // datetime(3)
		[Nullable,                DataMember                         ] public DateTime? HireDate        { get; set; } // datetime(3)
		[Nullable,                DataMember, MaxLength(60)          ] public string    Address         { get; set; } // nvarchar(60)
		[Nullable,                DataMember, MaxLength(15)          ] public string    City            { get; set; } // nvarchar(15)
		[Nullable,                DataMember, MaxLength(15)          ] public string    Region          { get; set; } // nvarchar(15)
		[Nullable,                DataMember, MaxLength(10)          ] public string    PostalCode      { get; set; } // nvarchar(10)
		[Nullable,                DataMember, MaxLength(15)          ] public string    Country         { get; set; } // nvarchar(15)
		[Nullable,                DataMember, MaxLength(24)          ] public string    HomePhone       { get; set; } // nvarchar(24)
		[Nullable,                DataMember, MaxLength(4)           ] public string    Extension       { get; set; } // nvarchar(4)
		[Nullable,                DataMember                         ] public byte[]    Photo           { get; set; } // image(2147483647)
		[Nullable,                DataMember, MaxLength(1073741823)  ] public string    Notes           { get; set; } // ntext(1073741823)
		[Nullable,                DataMember                         ] public int?      ReportsTo       { get; set; } // int(10)
		[Nullable,                DataMember, MaxLength(255)         ] public string    PhotoPath       { get; set; } // nvarchar(255)

		// FK_Employees_Employees
		[Association(ThisKey="ReportsTo", OtherKey="EmployeeID", CanBeNull=true)]
		public Employee FK_Employees_Employees { get; set; }

		// FK_Orders_Employees_BackReference
		[Association(ThisKey="EmployeeID", OtherKey="EmployeeID", CanBeNull=true)]
		public IEnumerable<Order> Orders { get; set; }

		// FK_EmployeeTerritories_Employees_BackReference
		[Association(ThisKey="EmployeeID", OtherKey="EmployeeID", CanBeNull=true)]
		public IEnumerable<EmployeeTerritory> EmployeeTerritories { get; set; }

		// FK_Employees_Employees_BackReference
		[Association(ThisKey="EmployeeID", OtherKey="ReportsTo", CanBeNull=true)]
		public IEnumerable<Employee> FK_Employees_Employees_BackReference { get; set; }
	}

	[Serializable, DataContract]
	[TableName(Name="EmployeeTerritories")]
	public partial class EmployeeTerritory
	{
		[PrimaryKey(1), DataMember, Required               ] public int    EmployeeID  { get; set; } // int(10)
		[PrimaryKey(2), DataMember, MaxLength(20), Required] public string TerritoryID { get; set; } // nvarchar(20)

		// FK_EmployeeTerritories_Employees
		[Association(ThisKey="EmployeeID", OtherKey="EmployeeID", CanBeNull=false)]
		public Employee Employee { get; set; }

		// FK_EmployeeTerritories_Territories
		[Association(ThisKey="TerritoryID", OtherKey="TerritoryID", CanBeNull=false)]
		public Territory Territory { get; set; }
	}

	// View
	[Serializable, DataContract]
	[TableName(Name="Invoices")]
	public partial class Invoice
	{
		[Nullable, DataMember, MaxLength(40)          ] public string    ShipName       { get; set; } // nvarchar(40)
		[Nullable, DataMember, MaxLength(60)          ] public string    ShipAddress    { get; set; } // nvarchar(60)
		[Nullable, DataMember, MaxLength(15)          ] public string    ShipCity       { get; set; } // nvarchar(15)
		[Nullable, DataMember, MaxLength(15)          ] public string    ShipRegion     { get; set; } // nvarchar(15)
		[Nullable, DataMember, MaxLength(10)          ] public string    ShipPostalCode { get; set; } // nvarchar(10)
		[Nullable, DataMember, MaxLength(15)          ] public string    ShipCountry    { get; set; } // nvarchar(15)
		[Nullable, DataMember, MaxLength(5)           ] public string    CustomerID     { get; set; } // nchar(5)
		[          DataMember, MaxLength(40), Required] public string    CustomerName   { get; set; } // nvarchar(40)
		[Nullable, DataMember, MaxLength(60)          ] public string    Address        { get; set; } // nvarchar(60)
		[Nullable, DataMember, MaxLength(15)          ] public string    City           { get; set; } // nvarchar(15)
		[Nullable, DataMember, MaxLength(15)          ] public string    Region         { get; set; } // nvarchar(15)
		[Nullable, DataMember, MaxLength(10)          ] public string    PostalCode     { get; set; } // nvarchar(10)
		[Nullable, DataMember, MaxLength(15)          ] public string    Country        { get; set; } // nvarchar(15)
		[          DataMember, MaxLength(31), Required] public string    Salesperson    { get; set; } // nvarchar(31)
		[          DataMember, Required               ] public int       OrderID        { get; set; } // int(10)
		[Nullable, DataMember                         ] public DateTime? OrderDate      { get; set; } // datetime(3)
		[Nullable, DataMember                         ] public DateTime? RequiredDate   { get; set; } // datetime(3)
		[Nullable, DataMember                         ] public DateTime? ShippedDate    { get; set; } // datetime(3)
		[          DataMember, MaxLength(40), Required] public string    ShipperName    { get; set; } // nvarchar(40)
		[          DataMember, Required               ] public int       ProductID      { get; set; } // int(10)
		[          DataMember, MaxLength(40), Required] public string    ProductName    { get; set; } // nvarchar(40)
		[          DataMember, Required               ] public decimal   UnitPrice      { get; set; } // money(19,4)
		[          DataMember, Required               ] public short     Quantity       { get; set; } // smallint(5)
		[          DataMember, Required               ] public float     Discount       { get; set; } // real(24)
		[Nullable, DataMember                         ] public decimal?  ExtendedPrice  { get; set; } // money(19,4)
		[Nullable, DataMember                         ] public decimal?  Freight        { get; set; } // money(19,4)
	}

	[Serializable, DataContract]
	[TableName(Name="Order Details")]
	public partial class OrderDetail
	{
		[PrimaryKey(1), DataMember, Required] public int     OrderID   { get; set; } // int(10)
		[PrimaryKey(2), DataMember, Required] public int     ProductID { get; set; } // int(10)
		[               DataMember, Required] public decimal UnitPrice { get; set; } // money(19,4)
		[               DataMember, Required] public short   Quantity  { get; set; } // smallint(5)
		[               DataMember, Required] public float   Discount  { get; set; } // real(24)

		// FK_Order_Details_Orders
		[Association(ThisKey="OrderID", OtherKey="OrderID", CanBeNull=false)]
		public Order OrderDetailsOrder { get; set; }

		// FK_Order_Details_Products
		[Association(ThisKey="ProductID", OtherKey="ProductID", CanBeNull=false)]
		public Product OrderDetailsProduct { get; set; }
	}

	// View
	[Serializable, DataContract]
	[TableName(Name="Order Details Extended")]
	public partial class OrderDetailsExtended
	{
		[          DataMember, Required               ] public int      OrderID       { get; set; } // int(10)
		[          DataMember, Required               ] public int      ProductID     { get; set; } // int(10)
		[          DataMember, MaxLength(40), Required] public string   ProductName   { get; set; } // nvarchar(40)
		[          DataMember, Required               ] public decimal  UnitPrice     { get; set; } // money(19,4)
		[          DataMember, Required               ] public short    Quantity      { get; set; } // smallint(5)
		[          DataMember, Required               ] public float    Discount      { get; set; } // real(24)
		[Nullable, DataMember                         ] public decimal? ExtendedPrice { get; set; } // money(19,4)
	}

	// View
	[Serializable, DataContract]
	[TableName(Name="Order Subtotals")]
	public partial class OrderSubtotal
	{
		[          DataMember, Required] public int      OrderID  { get; set; } // int(10)
		[Nullable, DataMember          ] public decimal? Subtotal { get; set; } // money(19,4)
	}

	[Serializable, DataContract]
	[TableName(Name="Orders")]
	public partial class Order
	{
		[Identity, PrimaryKey(1), DataMember, Required     ] public int       OrderID        { get; set; } // int(10)
		[Nullable,                DataMember, MaxLength(5) ] public string    CustomerID     { get; set; } // nchar(5)
		[Nullable,                DataMember               ] public int?      EmployeeID     { get; set; } // int(10)
		[Nullable,                DataMember               ] public DateTime? OrderDate      { get; set; } // datetime(3)
		[Nullable,                DataMember               ] public DateTime? RequiredDate   { get; set; } // datetime(3)
		[Nullable,                DataMember               ] public DateTime? ShippedDate    { get; set; } // datetime(3)
		[Nullable,                DataMember               ] public int?      ShipVia        { get; set; } // int(10)
		[Nullable,                DataMember               ] public decimal?  Freight        { get; set; } // money(19,4)
		[Nullable,                DataMember, MaxLength(40)] public string    ShipName       { get; set; } // nvarchar(40)
		[Nullable,                DataMember, MaxLength(60)] public string    ShipAddress    { get; set; } // nvarchar(60)
		[Nullable,                DataMember, MaxLength(15)] public string    ShipCity       { get; set; } // nvarchar(15)
		[Nullable,                DataMember, MaxLength(15)] public string    ShipRegion     { get; set; } // nvarchar(15)
		[Nullable,                DataMember, MaxLength(10)] public string    ShipPostalCode { get; set; } // nvarchar(10)
		[Nullable,                DataMember, MaxLength(15)] public string    ShipCountry    { get; set; } // nvarchar(15)

		// FK_Orders_Customers
		[Association(ThisKey="CustomerID", OtherKey="CustomerID", CanBeNull=true)]
		public Customer Customer { get; set; }

		// FK_Orders_Employees
		[Association(ThisKey="EmployeeID", OtherKey="EmployeeID", CanBeNull=true)]
		public Employee Employee { get; set; }

		// FK_Orders_Shippers
		[Association(ThisKey="ShipVia", OtherKey="ShipperID", CanBeNull=true)]
		public Shipper Shipper { get; set; }

		// FK_Order_Details_Orders_BackReference
		[Association(ThisKey="OrderID", OtherKey="OrderID", CanBeNull=true)]
		public IEnumerable<OrderDetail> OrderDetails { get; set; }
	}

	// View
	[Serializable, DataContract]
	[TableName(Name="Orders Qry")]
	public partial class OrdersQry
	{
		[          DataMember, Required               ] public int       OrderID        { get; set; } // int(10)
		[Nullable, DataMember, MaxLength(5)           ] public string    CustomerID     { get; set; } // nchar(5)
		[Nullable, DataMember                         ] public int?      EmployeeID     { get; set; } // int(10)
		[Nullable, DataMember                         ] public DateTime? OrderDate      { get; set; } // datetime(3)
		[Nullable, DataMember                         ] public DateTime? RequiredDate   { get; set; } // datetime(3)
		[Nullable, DataMember                         ] public DateTime? ShippedDate    { get; set; } // datetime(3)
		[Nullable, DataMember                         ] public int?      ShipVia        { get; set; } // int(10)
		[Nullable, DataMember                         ] public decimal?  Freight        { get; set; } // money(19,4)
		[Nullable, DataMember, MaxLength(40)          ] public string    ShipName       { get; set; } // nvarchar(40)
		[Nullable, DataMember, MaxLength(60)          ] public string    ShipAddress    { get; set; } // nvarchar(60)
		[Nullable, DataMember, MaxLength(15)          ] public string    ShipCity       { get; set; } // nvarchar(15)
		[Nullable, DataMember, MaxLength(15)          ] public string    ShipRegion     { get; set; } // nvarchar(15)
		[Nullable, DataMember, MaxLength(10)          ] public string    ShipPostalCode { get; set; } // nvarchar(10)
		[Nullable, DataMember, MaxLength(15)          ] public string    ShipCountry    { get; set; } // nvarchar(15)
		[          DataMember, MaxLength(40), Required] public string    CompanyName    { get; set; } // nvarchar(40)
		[Nullable, DataMember, MaxLength(60)          ] public string    Address        { get; set; } // nvarchar(60)
		[Nullable, DataMember, MaxLength(15)          ] public string    City           { get; set; } // nvarchar(15)
		[Nullable, DataMember, MaxLength(15)          ] public string    Region         { get; set; } // nvarchar(15)
		[Nullable, DataMember, MaxLength(10)          ] public string    PostalCode     { get; set; } // nvarchar(10)
		[Nullable, DataMember, MaxLength(15)          ] public string    Country        { get; set; } // nvarchar(15)
	}

	// View
	[Serializable, DataContract]
	[TableName(Name="Product Sales for 1997")]
	public partial class ProductSalesFor1997
	{
		[          DataMember, MaxLength(15), Required] public string   CategoryName { get; set; } // nvarchar(15)
		[          DataMember, MaxLength(40), Required] public string   ProductName  { get; set; } // nvarchar(40)
		[Nullable, DataMember                         ] public decimal? ProductSales { get; set; } // money(19,4)
	}

	[Serializable, DataContract]
	[TableName(Name="Products")]
	public partial class Product
	{
		[Identity, PrimaryKey(1), DataMember, Required               ] public int      ProductID       { get; set; } // int(10)
		[                         DataMember, MaxLength(40), Required] public string   ProductName     { get; set; } // nvarchar(40)
		[Nullable,                DataMember                         ] public int?     SupplierID      { get; set; } // int(10)
		[Nullable,                DataMember                         ] public int?     CategoryID      { get; set; } // int(10)
		[Nullable,                DataMember, MaxLength(20)          ] public string   QuantityPerUnit { get; set; } // nvarchar(20)
		[Nullable,                DataMember                         ] public decimal? UnitPrice       { get; set; } // money(19,4)
		[Nullable,                DataMember                         ] public short?   UnitsInStock    { get; set; } // smallint(5)
		[Nullable,                DataMember                         ] public short?   UnitsOnOrder    { get; set; } // smallint(5)
		[Nullable,                DataMember                         ] public short?   ReorderLevel    { get; set; } // smallint(5)
		[                         DataMember, Required               ] public bool     Discontinued    { get; set; } // bit

		// FK_Products_Categories
		[Association(ThisKey="CategoryID", OtherKey="CategoryID", CanBeNull=true)]
		public Category Category { get; set; }

		// FK_Products_Suppliers
		[Association(ThisKey="SupplierID", OtherKey="SupplierID", CanBeNull=true)]
		public Supplier Supplier { get; set; }

		// FK_Order_Details_Products_BackReference
		[Association(ThisKey="ProductID", OtherKey="ProductID", CanBeNull=true)]
		public IEnumerable<OrderDetail> OrderDetails { get; set; }
	}

	// View
	[Serializable, DataContract]
	[TableName(Name="Products Above Average Price")]
	public partial class ProductsAboveAveragePrice
	{
		[          DataMember, MaxLength(40), Required] public string   ProductName { get; set; } // nvarchar(40)
		[Nullable, DataMember                         ] public decimal? UnitPrice   { get; set; } // money(19,4)
	}

	// View
	[Serializable, DataContract]
	[TableName(Name="Products by Category")]
	public partial class ProductsByCategory
	{
		[          DataMember, MaxLength(15), Required] public string CategoryName    { get; set; } // nvarchar(15)
		[          DataMember, MaxLength(40), Required] public string ProductName     { get; set; } // nvarchar(40)
		[Nullable, DataMember, MaxLength(20)          ] public string QuantityPerUnit { get; set; } // nvarchar(20)
		[Nullable, DataMember                         ] public short? UnitsInStock    { get; set; } // smallint(5)
		[          DataMember, Required               ] public bool   Discontinued    { get; set; } // bit
	}

	// View
	[Serializable, DataContract]
	[TableName(Name="Quarterly Orders")]
	public partial class QuarterlyOrder
	{
		[Nullable, DataMember, MaxLength(5) ] public string CustomerID  { get; set; } // nchar(5)
		[Nullable, DataMember, MaxLength(40)] public string CompanyName { get; set; } // nvarchar(40)
		[Nullable, DataMember, MaxLength(15)] public string City        { get; set; } // nvarchar(15)
		[Nullable, DataMember, MaxLength(15)] public string Country     { get; set; } // nvarchar(15)
	}

	[Serializable, DataContract]
	[TableName(Name="Region")]
	public partial class Region
	{
		[PrimaryKey(1), DataMember, Required               ] public int    RegionID          { get; set; } // int(10)
		[               DataMember, MaxLength(50), Required] public string RegionDescription { get; set; } // nchar(50)

		// FK_Territories_Region_BackReference
		[Association(ThisKey="RegionID", OtherKey="RegionID", CanBeNull=true)]
		public IEnumerable<Territory> Territories { get; set; }
	}

	// View
	[Serializable, DataContract]
	[TableName(Name="Sales by Category")]
	public partial class SalesByCategory
	{
		[          DataMember, Required               ] public int      CategoryID   { get; set; } // int(10)
		[          DataMember, MaxLength(15), Required] public string   CategoryName { get; set; } // nvarchar(15)
		[          DataMember, MaxLength(40), Required] public string   ProductName  { get; set; } // nvarchar(40)
		[Nullable, DataMember                         ] public decimal? ProductSales { get; set; } // money(19,4)
	}

	// View
	[Serializable, DataContract]
	[TableName(Name="Sales Totals by Amount")]
	public partial class SalesTotalsByAmount
	{
		[Nullable, DataMember                         ] public decimal?  SaleAmount  { get; set; } // money(19,4)
		[          DataMember, Required               ] public int       OrderID     { get; set; } // int(10)
		[          DataMember, MaxLength(40), Required] public string    CompanyName { get; set; } // nvarchar(40)
		[Nullable, DataMember                         ] public DateTime? ShippedDate { get; set; } // datetime(3)
	}

	[Serializable, DataContract]
	[TableName(Name="Shippers")]
	public partial class Shipper
	{
		[Identity, PrimaryKey(1), DataMember, Required               ] public int    ShipperID   { get; set; } // int(10)
		[                         DataMember, MaxLength(40), Required] public string CompanyName { get; set; } // nvarchar(40)
		[Nullable,                DataMember, MaxLength(24)          ] public string Phone       { get; set; } // nvarchar(24)

		// FK_Orders_Shippers_BackReference
		[Association(ThisKey="ShipperID", OtherKey="ShipVia", CanBeNull=true)]
		public IEnumerable<Order> Orders { get; set; }
	}

	// View
	[Serializable, DataContract]
	[TableName(Name="Summary of Sales by Quarter")]
	public partial class SummaryOfSalesByQuarter
	{
		[Nullable, DataMember          ] public DateTime? ShippedDate { get; set; } // datetime(3)
		[          DataMember, Required] public int       OrderID     { get; set; } // int(10)
		[Nullable, DataMember          ] public decimal?  Subtotal    { get; set; } // money(19,4)
	}

	// View
	[Serializable, DataContract]
	[TableName(Name="Summary of Sales by Year")]
	public partial class SummaryOfSalesByYear
	{
		[Nullable, DataMember          ] public DateTime? ShippedDate { get; set; } // datetime(3)
		[          DataMember, Required] public int       OrderID     { get; set; } // int(10)
		[Nullable, DataMember          ] public decimal?  Subtotal    { get; set; } // money(19,4)
	}

	[Serializable, DataContract]
	[TableName(Name="Suppliers")]
	public partial class Supplier
	{
		[Identity, PrimaryKey(1), DataMember, Required               ] public int    SupplierID   { get; set; } // int(10)
		[                         DataMember, MaxLength(40), Required] public string CompanyName  { get; set; } // nvarchar(40)
		[Nullable,                DataMember, MaxLength(30)          ] public string ContactName  { get; set; } // nvarchar(30)
		[Nullable,                DataMember, MaxLength(30)          ] public string ContactTitle { get; set; } // nvarchar(30)
		[Nullable,                DataMember, MaxLength(60)          ] public string Address      { get; set; } // nvarchar(60)
		[Nullable,                DataMember, MaxLength(15)          ] public string City         { get; set; } // nvarchar(15)
		[Nullable,                DataMember, MaxLength(15)          ] public string Region       { get; set; } // nvarchar(15)
		[Nullable,                DataMember, MaxLength(10)          ] public string PostalCode   { get; set; } // nvarchar(10)
		[Nullable,                DataMember, MaxLength(15)          ] public string Country      { get; set; } // nvarchar(15)
		[Nullable,                DataMember, MaxLength(24)          ] public string Phone        { get; set; } // nvarchar(24)
		[Nullable,                DataMember, MaxLength(24)          ] public string Fax          { get; set; } // nvarchar(24)
		[Nullable,                DataMember, MaxLength(1073741823)  ] public string HomePage     { get; set; } // ntext(1073741823)

		// FK_Products_Suppliers_BackReference
		[Association(ThisKey="SupplierID", OtherKey="SupplierID", CanBeNull=true)]
		public IEnumerable<Product> Products { get; set; }
	}

	[Serializable, DataContract]
	[TableName(Name="Territories")]
	public partial class Territory
	{
		[PrimaryKey(1), DataMember, MaxLength(20), Required] public string TerritoryID          { get; set; } // nvarchar(20)
		[               DataMember, MaxLength(50), Required] public string TerritoryDescription { get; set; } // nchar(50)
		[               DataMember, Required               ] public int    RegionID             { get; set; } // int(10)

		// FK_Territories_Region
		[Association(ThisKey="RegionID", OtherKey="RegionID", CanBeNull=false)]
		public Region Region { get; set; }

		// FK_EmployeeTerritories_Territories_BackReference
		[Association(ThisKey="TerritoryID", OtherKey="TerritoryID", CanBeNull=true)]
		public IEnumerable<EmployeeTerritory> EmployeeTerritories { get; set; }
	}
}
