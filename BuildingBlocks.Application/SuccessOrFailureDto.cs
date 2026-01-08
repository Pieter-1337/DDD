namespace BuildingBlocks.Application
{
    public class SuccessOrFailureDto
    {
        public bool Success { get; set; }
        public string Message { get; set; }

        /// <summary>
        /// Update this dto with values of another dto
        /// </summary>
        /// <param name="dto">Other dto which values should be used for updating current dto</param>
        public void Update(SuccessOrFailureDto dto)
        {
            Success = Success && dto.Success;
            Message = string.IsNullOrWhiteSpace(Message) ? dto.Message : string.Join("\r\n", Message, dto.Message);
        }
    }
}
