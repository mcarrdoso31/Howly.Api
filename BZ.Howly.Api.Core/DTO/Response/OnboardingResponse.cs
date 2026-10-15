namespace BZ.Howly.Api.Core.DTO.Response;

/// <summary>
/// The first steps of a consultancy, each done once what it asks for exists: the consultancy's data (name, CNPJ and
/// e-mail), an agency, a process type, a customer, a process and someone else on the team (optional).
/// </summary>
public sealed record OnboardingResponse(bool CompanyProfile, bool Agencies, bool ProcessTypes, bool Customers, bool Processes, bool Team);
