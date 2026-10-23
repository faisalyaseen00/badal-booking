namespace BadalBooking.Api.Domain;

// Allowed transitions (enforced with conditional SQL updates in steps 1.5–1.7):
//
//   PendingPayment ──pay──────▶ Paid ──accept──▶ Accepted   (final)
//        │                       │
//        ├──cancel──▶ Cancelled ◀┘ cancel (only before a performer accepts)
//        └──30 min unpaid──▶ Expired
public enum BookingStatus
{
    PendingPayment,
    Paid,
    Accepted,
    Cancelled,
    Expired,
}
