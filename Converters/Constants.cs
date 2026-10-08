using System;
using System.Linq;
using System.Security.Claims;

namespace AvaloniaFingerprint.Converters
{
    public static class AppConstant
    {
        private static string foundMsg = "Data Found";
        private static string notFoundMsg = "Data Not Found";
        private static string requiredMsg = "{0} Is Required";
        private static string alreadyexistMsg = "{0} Already Exist";
        private static string createdsuccessMsg = "{0} {1} Created Successfully";
        private static string updatesuccessMsg = "{0} Updated Successfully";
        private static string failedMsg = "Failed To {0} Data For {1} {2}";

        public static string FoundMsg { get => foundMsg; set => foundMsg = value; }
        public static string NotFoundMsg { get => notFoundMsg; set => notFoundMsg = value; }
        public static string RequiredMsg { get => requiredMsg; set => requiredMsg = value; }
        public static string AlreadyExistMsg { get => alreadyexistMsg; set => alreadyexistMsg = value; }
        public static string CreatedSuccessMsg { get => createdsuccessMsg; set => createdsuccessMsg = value; }
        public static string UpdateSuccessMsg { get => updatesuccessMsg; set => updatesuccessMsg = value; }
        public static string FailedMsg { get => failedMsg; set => failedMsg = value; }
    }

    public static class DateFormat
    {
        private static string shortyearpattern = "yyMMdd";
        private static string longyearpattern = "yyyy-MM-dd HH:mm:ss";
        private static string yearmonthdate = "yyyy-MM-dd";
        private static string date = "dd/MM/yyyy";
        private static string datetime = "dd/MM/yyyy HH:mm";
        private static string datetimesecond = "dd/MM/yyyy HH:mm:ss";
        private static string datelong = "dd MMM yyyy";
        private static string dateshortmonth = "dd-MMM-yyyy";
        private static string longdatepattern = "dd-MMM-yyyy HH:mm:ss";
        private static string datehourminute = "dd/MM/yyyy HH:mm";
        private static string dateshortmonthhourminute = "dd-MMM-yyyy HH:mm";
        private static string hourmin = "HH:mm";
        private static string month = "MM";
        private static string daydatemonthyear = "dddd, dd MMMM yyyy";
        private static string day = "dddd";
        private static string year = "yyyy";
        private static string hourminutesecond = "HH:mm:ss";

        public static string Shortyearpattern { get => shortyearpattern; set => shortyearpattern = value; }
        public static string Longyearpattern { get => longyearpattern; set => longyearpattern = value; }
        public static string Date { get => date; set => date = value; }
        public static string Datetime { get => datetime; set => datetime = value; }
        public static string Datetimesecond { get => datetimesecond; set => datetimesecond = value; }
        public static string Datelong { get => datelong; set => datelong = value; }
        public static string Dateshortmonth { get => dateshortmonth; set => dateshortmonth = value; }
        public static string Longdatepattern { get => longdatepattern; set => longdatepattern = value; }
        public static string Datehourminute { get => datehourminute; set => datehourminute = value; }
        public static string Dateshortmonthhourminute { get => dateshortmonthhourminute; set => dateshortmonthhourminute = value; }
        public static string Hourmin { get => hourmin; set => hourmin = value; }
        public static string Month { get => month; set => month = value; }
        public static string Yearmonthdate { get => yearmonthdate; set => yearmonthdate = value; }
        public static string Daydatemonthyear { get => daydatemonthyear; set => daydatemonthyear = value; }
        public static string Day { get => day; set => day = value; }
        public static string Year { get => year; set => year = value; }
        public static string Hourminutesecond { get => hourminutesecond; set => hourminutesecond = value; }
    }

    public static class TransactionCode
    {
        private const string sales = "101";
        private const string salesReturn = "102";
        private const string registration = "001";
        private const string appointment = "002";
        private const string charges = "003";
        private const string jobOrder = "005";
        private const string externalJobOrder = "006";
        private const string patientTransfer = "007";
        private const string payment = "016";
        private const string paymentReturn = "017";
        private const string downPayment = "018";
        private const string downPaymentReturn = "019";
        private const string paymentReceipt = "032";
        private const string purchaseRequest = "034";
        private const string priceQuoteRequest = "035";
        private const string purchaseOrder = "037";
        private const string purchaseOrderReceive = "040";
        private const string grantsReceive = "041";
        private const string purchaseOrderReturn = "043";
        private const string receiptOfSubstitute = "044";
        private const string consignmentReceive = "045";
        private const string distribution = "050";
        private const string distributionConfirm = "055";
        private const string distributionRequest = "057";
        private const string requestNonStockItems = "058";
        private const string confirmationRequestNonStockItems = "059";
        private const string consignmentTransfer = "060";
        private const string stockConsumption = "073";
        private const string stockAdjustment = "074";
        private const string stockTaking = "075";
        private const string inventoryIssueOut = "082";
        private const string inventoryIssueRequestOut = "084";
        private const string inventoryIssueOutForOtherUnit = "085";
        private const string itemTariffRequest = "086";
        private const string prescription = "091";
        private const string dischargePrescription = "092";
        private const string spectaclePrescription = "093";
        private const string prescriptionReturn = "094";
        private const string directPurchase = "095";
        private const string referralOrder = "098";
        private const string packageJobOrder = "131";
        private const string accountPayable = "140";
        private const string accountReceivable = "150";
        private const string physicianLeave = "099";
        private const string productionOfGoods = "046";
        private const string consignmentReturn = "047";
        private const string salesToBranch = "048";
        private const string salesToBranchReturn = "049";
        private const string registrationInfo = "100";
        private const string budgetPlan = "140";
        private const string nonPatientCustomerharges = "008";
        private const string snackOrder = "132";
        private const string destructionOfExpiredItems = "083";
        private const string assetWorkOrder = "201";
        private const string assetWorkOrderRealization = "202";
        private const string assetPM = "203";
        private const string sanitationWorkOrder = "204";
        private const string sanitationMaintenanceActivity = "205";
        private const string budgetPlanApproval = "141";
        private const string assetId = "200";
        private const string cssd = "301";
        private const string remunerationByIdi = "142";
        private const string remunerationEmp = "143";
        private const string laundryWashingProcess = "501";
        private const string linenItemsExtermination = "502";
        private const string bookingSchedule = "503";

        public static string Sales => sales;
        public static string SalesReturn => salesReturn;
        public static string Registration => registration;
        public static string Appointment => appointment;
        public static string Charges => charges;
        public static string JobOrder => jobOrder;
        public static string ExternalJobOrder => externalJobOrder;
        public static string PatientTransfer => patientTransfer;
        public static string Payment => payment;
        public static string PaymentReturn => paymentReturn;
        public static string DownPayment => downPayment;
        public static string DownPaymentReturn => downPaymentReturn;
        public static string PaymentReceipt => paymentReceipt;
        public static string PurchaseRequest => purchaseRequest;
        public static string PriceQuoteRequest => priceQuoteRequest;
        public static string PurchaseOrder => purchaseOrder;
        public static string PurchaseOrderReceive => purchaseOrderReceive;
        public static string GrantsReceive => grantsReceive;
        public static string PurchaseOrderReturn => purchaseOrderReturn;
        public static string ReceiptOfSubstitute => receiptOfSubstitute;
        public static string ConsignmentReceive => consignmentReceive;
        public static string Distribution => distribution;
        public static string DistributionConfirm => distributionConfirm;
        public static string DistributionRequest => distributionRequest;
        public static string RequestNonStockItems => requestNonStockItems;
        public static string ConfirmationRequestNonStockItems => confirmationRequestNonStockItems;
        public static string ConsignmentTransfer => consignmentTransfer;
        public static string StockConsumption => stockConsumption;
        public static string StockAdjustment => stockAdjustment;
        public static string StockTaking => stockTaking;
        public static string InventoryIssueOut => inventoryIssueOut;
        public static string InventoryIssueRequestOut => inventoryIssueRequestOut;
        public static string InventoryIssueOutForOtherUnit => inventoryIssueOutForOtherUnit;
        public static string ItemTariffRequest => itemTariffRequest;
        public static string Prescription => prescription;
        public static string DischargePrescription => dischargePrescription;
        public static string SpectaclePrescription => spectaclePrescription;
        public static string PrescriptionReturn => prescriptionReturn;
        public static string DirectPurchase => directPurchase;
        public static string ReferralOrder => referralOrder;
        public static string PackageJobOrder => packageJobOrder;
        public static string AccountPayable => accountPayable;
        public static string AccountReceivable => accountReceivable;
        public static string PhysicianLeave => physicianLeave;
        public static string ProductionOfGoods => productionOfGoods;
        public static string ConsignmentReturn => consignmentReturn;
        public static string SalesToBranch => salesToBranch;
        public static string SalesToBranchReturn => salesToBranchReturn;
        public static string RegistrationInfo => registrationInfo;
        public static string BudgetPlan => budgetPlan;
        public static string NonPatientCustomerharges => nonPatientCustomerharges;
        public static string SnackOrder => snackOrder;
        public static string DestructionOfExpiredItems => destructionOfExpiredItems;
        public static string AssetWorkOrder => assetWorkOrder;
        public static string AssetWorkOrderRealization => assetWorkOrderRealization;
        public static string AssetPM => assetPM;
        public static string SanitationWorkOrder => sanitationWorkOrder;
        public static string SanitationMaintenanceActivity => sanitationMaintenanceActivity;
        public static string BudgetPlanApproval => budgetPlanApproval;
        public static string AssetId => assetId;
        public static string Cssd => cssd;
        public static string RemunerationByIdi => remunerationByIdi;
        public static string RemunerationEmp => remunerationEmp;
        public static string LaundryWashingProcess => laundryWashingProcess;
        public static string LinenItemsExtermination => linenItemsExtermination;
        public static string BookingSchedule => bookingSchedule;
    }

    public static class UserClaim
    {
        public static bool HasProgram(this ClaimsPrincipal user, string programId)
        {
            return user.Claims.Any(x => x.Type == "programId" && x.Value == programId);
        }

        public static bool HasAnyProgram(this ClaimsPrincipal user, params string[] programIds)
        {
            return user.Claims.Any(x => x.Type == "programId" && programIds.Contains(x.Value, StringComparer.OrdinalIgnoreCase));
        }
    }

    public static class ProgramID
    {
        private static string patientmanagement = "01";
        private static string systemauthorization = "95.01";
        private static string myhome = "99.99.99";
        private static string healthrecord = "02.01.26";
        private static string prescriptionreview = "04.04.02";
        private static string user = "95.01.02";
        private static string paramedicschedule = "01.03.10";
        private static string admitting = "01.02.01";
        private static string outpatient = "01.01.02";
        private static string emergency = "01.05.01";
        private static string medicalcheckup = "01.17.02";

        public static string Patientmanagement { get => patientmanagement; set => patientmanagement = value; }
        public static string Systemauthorization { get => systemauthorization; set => systemauthorization = value; }
        public static string Myhome { get => myhome; set => myhome = value; }
        public static string Healthrecord { get => healthrecord; set => healthrecord = value; }
        public static string Prescriptionreview { get => prescriptionreview; set => prescriptionreview = value; }
        public static string User { get => user; set => user = value; }
        public static string Paramedicschedule { get => paramedicschedule; set => paramedicschedule = value; }
        public static string Admitting { get => admitting; set => admitting = value; }
        public static string Outpatient { get => outpatient; set => outpatient = value; }
        public static string Emergency { get => emergency; set => emergency = value; }
        public static string Medicalcheckup { get => medicalcheckup; set => medicalcheckup = value; }
    }

    public static class UserType
    {
        private static string administration = "ADM";
        private static string doctor = "DTR";
        private static string nurse = "NRS";
        private static string nutrition = "NUT";
        private static string physiotherapy = "PHY";
        private static string pharmacy = "FAR";
        private static string rehabilitation = "RHB";
        private static string registration = "REG";
        private static string labanalyst = "AL";

        public static string Administration { get => administration; set => administration = value; }
        public static string Doctor { get => doctor; set => doctor = value; }
        public static string Nutrition { get => nutrition; set => nutrition = value; }
        public static string Physiotherapy { get => physiotherapy; set => physiotherapy = value; }
        public static string Pharmacy { get => pharmacy; set => pharmacy = value; }
        public static string Rehabilitation { get => rehabilitation; set => rehabilitation = value; }
        public static string Registration { get => registration; set => registration = value; }
        public static string Labanalyst { get => labanalyst; set => labanalyst = value; }
        public static string Nurse { get => nurse; set => nurse = value; }
    }
}
