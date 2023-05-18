using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;
using HotelMan_HotelAdmin.Models;
using HttpMultipartParser;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text;
using System.Text.Json;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace HotelMan_HotelAdmin
{
    public class HotelAdmin
    {
        public async Task<APIGatewayProxyResponse> ListHotels(APIGatewayProxyRequest request)
        {
            // query string parameter called token is passed to this lambda method.
            var response = new APIGatewayProxyResponse()
            {
                Headers = new Dictionary<string, string>(),
                StatusCode = 200
            };

            try
            {                
                response.Headers.Add("Access-Control-Allow-Origin", "*");
                response.Headers.Add("Access-Control-Allow-Headers", "*");
                response.Headers.Add("Access-Control-Allow-Methods", "OPTIONS,GET");
                response.Headers.Add("Content-Type", "application/json");

                var token = request.QueryStringParameters["token"];
                var tokenDetails = new JwtSecurityToken(token);
                var userId = tokenDetails.Claims.FirstOrDefault(x => x.Type == "sub");

                Console.WriteLine($"Token: {token}");
                Console.WriteLine($"UserId: {userId}");

                var region = Environment.GetEnvironmentVariable("AWS_REGION");
                var dbClient = new AmazonDynamoDBClient(RegionEndpoint.GetBySystemName(region));
                using var dbContext = new DynamoDBContext(dbClient);

                var hotel = await dbContext.ScanAsync<Hotel>(
                    new[] { new ScanCondition("UserId", ScanOperator.Equal, userId.Value) }
                    ).GetRemainingAsync();

                response.Body = JsonSerializer.Serialize(hotel);
            }
            catch (Exception ex)
            {
                response.Body = ex.Message;
            }

            return response;
        }

        public async Task<APIGatewayProxyResponse> AddHotel(APIGatewayProxyRequest request, ILambdaContext context)
        {
            var response = new APIGatewayProxyResponse()
            {
                Headers = new Dictionary<string, string>(),
                StatusCode = 200
            };

            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Headers", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "OPTIONS,POST");

            var bodyContent = request.IsBase64Encoded ? Convert.FromBase64String(request.Body) : Encoding.UTF8.GetBytes(request.Body);

            using var memStream = new MemoryStream(bodyContent);
            var formData = MultipartFormDataParser.Parse(memStream);

            var hotelName = formData.GetParameterValue("hotelName");
            var hotelRating = formData.GetParameterValue("hotelRating");
            var hotelCity = formData.GetParameterValue("hotelCity");
            var hotelPrice = formData.GetParameterValue("hotelPrice");

            var file = formData.Files.FirstOrDefault();
            var fileName = file.FileName;

            //await using var fileContentStream = new MemoryStream();
            //await file.Data.CopyToAsync(fileContentStream);
            //fileContentStream.Position = 0;

            file.Data.Position = 0;

            Console.WriteLine($"File Size: {file.Data.Length}");

            var userId = formData.GetParameterValue("userId");
            var idToken = formData.GetParameterValue("idToken");

            // Could be
            //var token = request.Headers["Authorization"].Replace("Bearer ", "");

            var token = new JwtSecurityToken(idToken);
            var group = token.Claims.FirstOrDefault(x => x.Type == "cognito:groups");

            if (group == null || group.Value != "Admin")
            {
                response.StatusCode = (int)HttpStatusCode.Unauthorized;
                response.Body = JsonSerializer.Serialize(new { Erro = "Unauthorize. Must be a member of Admin group." });
            }

            var region = Environment.GetEnvironmentVariable("AWS_REGION");
            var bucketName = Environment.GetEnvironmentVariable("bucketName");

            var client = new AmazonS3Client(RegionEndpoint.GetBySystemName(region));
            var dbClient = new AmazonDynamoDBClient(RegionEndpoint.GetBySystemName(region));

            try
            {
                await client.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = fileName,
                    InputStream = file.Data
                });

                var hotel = new Hotel
                {
                    UserId = userId,
                    Id = Guid.NewGuid().ToString(),
                    Name = hotelName,
                    CityName = hotelCity,
                    Price = int.Parse(hotelPrice),
                    Rating = int.Parse(hotelRating),
                    FileName = fileName
                };

                using var dbContext = new DynamoDBContext(dbClient);
                await dbContext.SaveAsync(hotel);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                throw;
            }

            Console.WriteLine("OK.");

            return response;
        }
    }
}