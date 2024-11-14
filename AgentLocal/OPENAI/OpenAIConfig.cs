using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AgentLocal.OPENAI
{
    public class OpenAIConfig
    {
        public string ApiKey { get; set; }
        public string ImageGenerationEndpoint { get; set; }
        public string DefaultImageSize { get; set; }
        public int MaxImagesPerRequest { get; set; }
    }
}
