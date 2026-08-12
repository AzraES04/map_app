using System.ComponentModel.DataAnnotations;

namespace StajProject.Business.DTOs;

public class LocationCreateDto
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>Boylam (longitude), örn. 32.8597 (Ankara)</summary>
    [Required]
    [Range(-180, 180)]
    public double Longitude { get; set; }

    /// <summary>Enlem (latitude), örn. 39.9334 (Ankara)</summary>
    [Required]
    [Range(-90, 90)]
    public double Latitude { get; set; }
}
