namespace Amadban.Data;

public sealed record OfficialPrepaymentPrint(
    long Number,string InvoiceNumber,string Date,string Code,string CodeTitle,string Purpose,
    string Sheba,string Bank,string Payee,long Amount,long? Document,string State,
    IReadOnlyDictionary<string,SignerIdentity> Signers,bool Petty,string Template,string Permit,string PermitDate,long Tax);
