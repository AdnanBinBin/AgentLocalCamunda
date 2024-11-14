using System.Text.Json;
using System.Net.Http;
using System.IO;
using Zeebe.Client;
using Zeebe.Client.Impl.Builder;
using Zeebe.Client.Api.Responses;
using Zeebe.Client.Api.Worker;
using Microsoft.Extensions.Options;
using AgentLocal.SMTP;
using System.Net.Mail;
using System.Text.Json.Serialization;
using AgentLocal.OPENAI;
using System.Net.Http.Json;
using AgentLocal.Data;
using AgentLocal.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

public class Camunda
{
    private IZeebeClient? _zeebeClient;
    private readonly IOptions<CamundaConfig> _options;
    private readonly IOptions<OpenAIConfig> _openAIOptions;
    private readonly IServiceProvider _services;
    private readonly MailService _mailService;
    private readonly HttpClient _httpClient;
    public Prototype prototypeToSave { get; set; }



    private class OpenAIImageResponse
    {
        public int Created { get; set; }
        public List<OpenAIImageData> Data { get; set; }
    }

    private class OpenAIImageData
    {
        public string Url { get; set; }
    }

    

    public Camunda(IServiceProvider services, IOptions<CamundaConfig> options, IOptions<OpenAIConfig> openAIOptions, MailService mailService)
    {
        _services = services;
        _options = options;
        _openAIOptions = openAIOptions;
        _mailService = mailService;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _openAIOptions.Value.ApiKey);


    }

    private async Task<string> GenerateImageWithOpenAI(ConceptionPlanFormData formData)
    {
        try
        {
            Console.WriteLine(formData.StorageCapacity);
            Console.WriteLine(formData.RAMSize);
            Console.WriteLine(formData.ProductDescription);
            var prompt = $@"{formData.ProductDescription} with :
            Storage: {formData.StorageCapacity}GB
            RAM: {formData.RAMSize}GB
             ";

            Console.WriteLine(prompt);

            // S'assurer que le Content-Type est correctement défini
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, _openAIOptions.Value.ImageGenerationEndpoint);
            requestMessage.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var requestBody = new
            {
                prompt = prompt,
                n = 1,
                size = "1024x1024"
            };

            requestMessage.Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                System.Text.Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.SendAsync(requestMessage);

            // Log pour déboguer
            Console.WriteLine($"Response status: {response.StatusCode}");
            var responseContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Response content: {responseContent}");

            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<OpenAIImageResponse>();
            return result?.Data?.FirstOrDefault()?.Url;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error generating image with OpenAI: {ex.Message}");
            return null;
        }
    }

    private async Task<string> DownloadImageToTempFile(string imageUrl)
    {
        try
        {
            // Créer une nouvelle requête spécifique pour le téléchargement
            var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
            // Ne pas ajouter le header Bearer pour cette requête car c'est une URL signée
            using var client = new HttpClient(); // Nouveau client sans auth header

            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var tempPath = Path.Combine(Path.GetTempPath(), $"conception_plan_image_{Guid.NewGuid()}.png");
            await File.WriteAllBytesAsync(tempPath, await response.Content.ReadAsByteArrayAsync());
            return tempPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error downloading image: {ex.Message}");
            return null;
        }
    }

    private async Task SendConceptionPlanJob(IJobClient client, IJob job)
    {
        string tempImagePath = null;
        try
        {
            var variables = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(job.Variables);

           

            var formData = new ConceptionPlanFormData
            {
                EmailRecipient = GetStringValue(variables, "emailRecipient"),
                ProductDescription = GetStringValue(variables, "productDescription"),
                ModelName = GetStringValue(variables, "modelName"),
                OperatingSystem = GetStringValue(variables, "operatingSystem"),
                ReleaseYear = GetIntValue(variables, "releaseYear"),
                ProcessorModel = GetStringValue(variables, "processorModel"),
                RAMSize = GetIntValue(variables, "ramSize"),
                StorageCapacity = GetIntValue(variables, "storageCapacity"),
                DualSIMSupport = GetBoolValue(variables, "dualSimSupport"),
                FormDate = DateTime.Now
            };

            var emailRecipient = formData.EmailRecipient;

            var emailBody = $@"
            <h2>Smartphone Realization Conception Plan</h2>
            
            <h3>Basic Information</h3>
            <ul>
                <li><strong>Model Name:</strong> {formData.ModelName}</li>
                <li><strong>Operating System:</strong> {formData.OperatingSystem}</li>
                <li><strong>Expected Release Year:</strong> {formData.ReleaseYear}</li>
            </ul>

            <h3>Hardware Details</h3>
            <ul>
                <li><strong>Processor Model:</strong> {formData.ProcessorModel}</li>
                <li><strong>Form Date:</strong> {formData.FormDate:yyyy-MM-dd}</li>
                <li><strong>RAM Size:</strong> {formData.RAMSize} GB</li>
                <li><strong>Storage Capacity:</strong> {formData.StorageCapacity} GB</li>
                <li><strong>Dual SIM Support:</strong> {(formData.DualSIMSupport ? "Yes" : "No")}</li>
            </ul>

            <p>This conception plan was automatically generated on {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>
        ";

            Console.WriteLine($"Sending email to: {formData.EmailRecipient}");
            string subject = $"New Conception Plan - {formData.ModelName}";

            // D'abord envoyer l'email
            try
            {
                await _mailService.SendEmailAsync(
                    emailRecipient,
                    subject,
                    emailBody,
                    isHtml: true
                );
            }
            catch (SmtpException ex)
            {
                Console.WriteLine($"SMTP Error: {ex.Message}");
                await client.NewThrowErrorCommand(job.Key)
                    .ErrorCode("InvalidEmail")
                    .ErrorMessage($"Failed to send email: {ex.Message}")
                    .Send();
                return;
            }

            var conceptionPlanJson = JsonSerializer.Serialize(formData);

            // Ensuite publier le message
            await _zeebeClient!.NewPublishMessageCommand()
                .MessageName("ExternalReceiver")
                .CorrelationKey(string.Empty)
                .Variables(JsonSerializer.Serialize(new
                {
                    conceptionPlan = conceptionPlanJson,
                    emailSent = true,
                }))
                .Send();

            // Compléter le job
            await client.NewCompleteJobCommand(job.Key)
                .Variables($"{{\"emailSent\": true, \"emailRecipient\": \"{emailRecipient}\"}}")
                .Send();

            Console.WriteLine($"Email sent successfully to {emailRecipient}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            await client.NewFailCommand(job.Key)
                .Retries(job.Retries - 1)
                .ErrorMessage(ex.Message)
                .Send();
        }
    }

    private async Task SendPrototypeJob(IJobClient client, IJob job)
    {
        string tempImagePath = null;

        try
        {
            Console.WriteLine("Starting SendPrototype job");
            var variables = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(job.Variables);

            // Vérification du traitement précédent
            if (variables.ContainsKey("prototypeGenerated") && variables["prototypeGenerated"].GetBoolean())
            {
                Console.WriteLine("This prototype has already been generated and sent. Skipping duplicate processing.");
                await client.NewCompleteJobCommand(job.Key)
                    .Variables("{\"prototypeGenerated\": true, \"emailSent\": true}")
                    .Send();
                return;
            }

            
                var conceptionPlanJson = variables["conceptionPlan"].ToString();

                // Désérialisation en objet ConceptionPlanFormData
                var formData = JsonSerializer.Deserialize<ConceptionPlanFormData>(conceptionPlanJson);




                // Construction des données du formulaire

                // Validation des données essentielles
                if (string.IsNullOrEmpty(formData.EmailRecipient) || string.IsNullOrEmpty(formData.ModelName))
                {
                    throw new Exception("Missing required fields: Email or Model Name");
                }

                // Génération de l'image
                var imageUrl = await GenerateImageWithOpenAI(formData);
                if (string.IsNullOrEmpty(imageUrl))
                {
                    throw new Exception("Failed to generate image with OpenAI");
                }

                // Téléchargement de l'image
                tempImagePath = await DownloadImageToTempFile(imageUrl);
                if (string.IsNullOrEmpty(tempImagePath))
                {
                    throw new Exception("Failed to download and save the image");
                }

                // Lecture des données de l'image
                byte[] originalImageData = await File.ReadAllBytesAsync(tempImagePath);
                byte[] imageData = CompressImage(originalImageData);

                // Préparation du corps de l'email
                var emailBody = $@"
        <h2>Smartphone Prototype Visualization</h2>
        
        <h3>Product Overview</h3>
        <p>{formData.ProductDescription}</p>

        <h3>Technical Specifications</h3>
        <ul>
            <li><strong>Model Name:</strong> {formData.ModelName}</li>
            <li><strong>Operating System:</strong> {formData.OperatingSystem}</li>
            <li><strong>Processor:</strong> {formData.ProcessorModel}</li>
            <li><strong>RAM:</strong> {formData.RAMSize} GB</li>
            <li><strong>Storage:</strong> {formData.StorageCapacity} GB</li>
            <li><strong>Dual SIM Support:</strong> {(formData.DualSIMSupport ? "Yes" : "No")}</li>
        </ul>

        <h3>AI-Generated Prototype Visualization</h3>
        <p>Please find attached an AI-generated visualization of your smartphone prototype based on the provided specifications.</p>

        <p>Generated on {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>";

                // Envoi de l'email
                Console.WriteLine($"Sending prototype visualization to: {formData.EmailRecipient}");

                try
                {
                    await _mailService.SendEmailWithAttachmentAsync(
                        formData.EmailRecipient,
                        $"Prototype Visualization - {formData.ModelName}",
                        emailBody,
                        tempImagePath,
                        isHtml: true
                    );
                }
                catch (SmtpException ex)
                {
                    throw new Exception($"Failed to send email: {ex.Message}");
                }

                // Création de l'objet prototype
                var prototype = new Prototype
                {
                    EmailRecipient = formData.EmailRecipient,
                    ModelName = formData.ModelName,
                    OperatingSystem = formData.OperatingSystem,
                    ReleaseYear = formData.ReleaseYear,
                    ProcessorModel = formData.ProcessorModel,
                    RAMSize = formData.RAMSize,
                    StorageCapacity = formData.StorageCapacity,
                    DualSIMSupport = formData.DualSIMSupport,
                    ProductDescription = formData.ProductDescription,
                    CreatedDate = DateTime.Now,
                    ImageData = imageData
                };

                // Sérialisation et envoi des données
                var prototypeJson = JsonSerializer.Serialize(prototype);

                // D'abord, publier le message pour la pool externe
                await _zeebeClient!.NewPublishMessageCommand()
                    .MessageName("PrototypeReceving") // Le nom du message qui doit correspondre à votre BPMN
                    .CorrelationKey(formData.ModelName) // Utiliser un identifiant unique
                    .Variables(JsonSerializer.Serialize(new
                    {
                        prototypeData = prototypeJson,
                        modelName = formData.ModelName,
                        prototypeGenerated = true,
                        emailSent = true
                    }))
                    .Send();

                // Ensuite, compléter le job local
                await client.NewCompleteJobCommand(job.Key)
                    .Variables(JsonSerializer.Serialize(new
                    {
                        prototypeGenerated = true,
                        emailSent = true
                    }))
                    .Send();

                Console.WriteLine($"Successfully completed job {job.Key} and sent message to external pool");

            }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in SendPrototypeJob: {ex.Message}");

            if (ex is SmtpException)
            {
                await client.NewThrowErrorCommand(job.Key)
                    .ErrorCode("EmailError")
                    .ErrorMessage($"Failed to send email: {ex.Message}")
                    .Send();
            }
            else
            {
                await client.NewFailCommand(job.Key)
                    .Retries(job.Retries - 1)
                    .ErrorMessage(ex.Message)
                    .Send();
            }
        }
    }

    private async Task SavePrototypeJob(IJobClient client, IJob job)
    {
        try
        {
            using (var scope = _services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<PrototypeDbContext>();

                Console.WriteLine("Starting SavePrototype job");

                var variables = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(job.Variables);

                // Récupérer les données du prototype
                if (!variables.ContainsKey("prototypeData"))
                {
                    throw new Exception("Prototype data not found in job variables");
                }

                string modelName = variables.ContainsKey("modelName")
                    ? variables["modelName"].GetString()
                    : "Unknown Model";

                var prototypeJson = variables["prototypeData"].GetString();
                if (string.IsNullOrEmpty(prototypeJson))
                {
                    throw new Exception("Prototype data is empty");
                }

                try
                {
                    var prototype = JsonSerializer.Deserialize<Prototype>(prototypeJson);

                    // Sauvegarder dans la base de données
                    dbContext.Prototypes.Add(prototype);
                    await dbContext.SaveChangesAsync();
                    Console.WriteLine("Prototype saved");

                    

                    // Publier le message de confirmation
                    await _zeebeClient!.NewPublishMessageCommand()
                        .MessageName("PrototypeSaved")
                        .CorrelationKey(modelName)
                        .Variables(JsonSerializer.Serialize(new
                        {
                            modelName = prototype.ModelName,
                            prototypeSaved = true,
                            saveDate = DateTime.Now
                        }))
                        .Send();

                    // Compléter le job
                    await client.NewCompleteJobCommand(job.Key)
                        .Variables(JsonSerializer.Serialize(new
                        {
                            prototypeSaved = true,
                            saveDate = DateTime.Now
                        }))
                        .Send();
                }
                catch (DbUpdateException dbEx)
                {
                    Console.WriteLine($"Database Error: {dbEx.Message}");
                    await client.NewFailCommand(job.Key)
                        .Retries(job.Retries - 1)
                        .ErrorMessage($"Failed to save prototype to database: {dbEx.Message}")
                        .Send();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in SavePrototypeJob: {ex.Message}");
            await client.NewFailCommand(job.Key)
                .Retries(job.Retries - 1)
                .ErrorMessage(ex.Message)
                .Send();
        }
    }

    private bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }


    private byte[] CompressImage(byte[] originalImage)
    {
        using (var ms = new MemoryStream())
        {
            using (var image = Image.Load(originalImage))
            {
                // Redimensionner si nécessaire
                if (image.Width > 1024 || image.Height > 1024)
                {
                    image.Mutate(x => x.Resize(1024, 1024));
                }

                // Sauvegarder avec compression JPEG
                image.Save(ms, new JpegEncoder
                {
                    Quality = 75  // Ajuster la qualité selon vos besoins (0-100)
                });
            }
            return ms.ToArray();
        }
    }

    private string GetStringValue(Dictionary<string, JsonElement> variables, string key)
    {
        return variables.ContainsKey(key) ? variables[key].GetString() : string.Empty;
    }

    private int GetIntValue(Dictionary<string, JsonElement> variables, string key)
    {
        return variables.ContainsKey(key) ? variables[key].GetInt32() : 0;
    }

    private bool GetBoolValue(Dictionary<string, JsonElement> variables, string key)
    {
        return variables.ContainsKey(key) && variables[key].GetBoolean();
    }

    public async Task Start()
    {
        try
        {
            Console.WriteLine("Starting Camunda");
            _zeebeClient = CamundaCloudClientBuilder
                .Builder()
                .UseClientId(_options.Value.ClientId)
                .UseClientSecret(_options.Value.ClientSecret)
                .UseContactPoint(_options.Value.ClusterAddress)
                .Build();

            // Vérifier la connexion
            Console.WriteLine($"Connected to Camunda Cloud");
            Console.WriteLine(_options.Value.ClientId);
            Console.WriteLine(_options.Value.ClientSecret);
            Console.WriteLine(_options.Value.ClusterAddress);

            try
            {
                var topology = await _zeebeClient.TopologyRequest().Send();
                Console.WriteLine($"Successfully connected to Zeebe cluster!");
                Console.WriteLine("Topology details:");
                foreach (var broker in topology.Brokers)
                {
                    Console.WriteLine($"Broker {broker.Host}:{broker.Port}");
                    foreach (var partition in broker.Partitions)
                    {
                        Console.WriteLine($"  Partition {partition.PartitionId} - Role: {partition.Role}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to connect to Zeebe: {ex.Message}");
                throw;
            }

            // Créer et démarrer les workers
            var conceptionPlanWorker = _zeebeClient.NewWorker()
                .JobType("SendConceptionPlan")
                .Handler(SendConceptionPlanJob)
                .MaxJobsActive(1)
                .Name("SendConceptionPlanWorker")
                .PollInterval(TimeSpan.FromSeconds(1))
                .Timeout(TimeSpan.FromSeconds(10))
                .Open();

            var prototypeWorker = _zeebeClient.NewWorker()
                .JobType("SendPrototype")
                .Handler(SendPrototypeJob)
                .MaxJobsActive(1)
                .Name("SendPrototypeWorker")
                .PollInterval(TimeSpan.FromSeconds(1))
                .Timeout(TimeSpan.FromSeconds(10))
                .Open();

            var savePrototypeWorker = _zeebeClient.NewWorker()
                .JobType("SavePrototype")
                .Handler(SavePrototypeJob)
                .MaxJobsActive(1)
                .Name("SavePrototypeWorker")
                .PollInterval(TimeSpan.FromSeconds(1))
                .Timeout(TimeSpan.FromSeconds(10))
                .Open();

            

            Console.WriteLine("Workers are now open and ready to process jobs");

            // Garder l'application en vie
            while (true)
            {
                await Task.Delay(1000);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in Camunda worker: {ex.Message}");
            throw;
        }
    }
}