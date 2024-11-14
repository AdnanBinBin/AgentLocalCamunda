using AgentLocal.Models;
using System;
using System.ComponentModel.DataAnnotations;

namespace AgentLocal.Models
{
    public class Prototype
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string ModelName { get; set; }

        [Required]
        [MaxLength(50)]
        public string OperatingSystem { get; set; }

        public int ReleaseYear { get; set; }

        [Required]
        [MaxLength(100)]
        public string ProcessorModel { get; set; }

        public int RAMSize { get; set; }

        public int StorageCapacity { get; set; }

        public bool DualSIMSupport { get; set; }

        [MaxLength(1000)]
        public string ProductDescription { get; set; }

        [Required]
        [MaxLength(255)]
        public string EmailRecipient { get; set; }

        public DateTime CreatedDate { get; set; }

        public byte[] ImageData { get; set; }
    }
}


    
