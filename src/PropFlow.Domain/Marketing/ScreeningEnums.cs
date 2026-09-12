namespace PropFlow.Domain.Marketing;

// The retry state of one screening request. Completed and Abandoned are terminal: a request
// that exhausted its attempts is not retried in place, a new request is created.
public enum ScreeningRequestStatus { Pending = 0, InFlight = 1, Completed = 2, Abandoned = 3 }

// The verdict vocabulary, shared by the domain and the IScreeningProvider port (PF-S05.03) so
// there is one set of names rather than a domain enum and a port enum that have to be mapped.
// Unavailable is the provider-outage marker and is deliberately distinct from Fail: a Fail is a
// real answer about the applicant and gets stored, an outage is no answer at all and PF-S05.06
// turns it into a 503 with nothing written. ScreeningResult refuses to store it.
public enum ScreeningRecommendation { Pass = 0, Review = 1, Fail = 2, Unavailable = 3 }
