namespace BadalBooking.Api.Domain;

public enum UserRole
{
    Customer,  // books Umrah for a pilgrim and pays
    Performer, // performs the Umrah; accepts paid bookings (only after an admin approved their application)
    Admin,     // approves or rejects performer applications; created only from server settings
}
