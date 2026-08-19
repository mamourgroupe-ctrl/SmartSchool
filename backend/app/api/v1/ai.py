from fastapi import APIRouter, HTTPException
from pydantic import BaseModel, Field

from app.ai.service import chat


router = APIRouter(
    prefix="/ai",
    tags=["AI"],
)


class ChatRequest(BaseModel):
    message: str = Field(
        ...,
        min_length=1,
        max_length=4000,
    )


class ChatResponse(BaseModel):
    response: str


@router.post("/chat", response_model=ChatResponse)
async def chat_endpoint(request: ChatRequest):
    try:
        response = chat(request.message)

        return ChatResponse(
            response=response,
        )

    except Exception as exc:
        raise HTTPException(
            status_code=500,
            detail=f"AI service error: {exc}",
        ) from exc
