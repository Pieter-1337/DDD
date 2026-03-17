/**
 * Base response for commands that return success/failure.
 * Maps to BuildingBlocks.Application.Dtos.SuccessOrFailureDto on the backend.
 */
export interface SuccessOrFailureResponse {
  success: boolean;
  message: string;
}
