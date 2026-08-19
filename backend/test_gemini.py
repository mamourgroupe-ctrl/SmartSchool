
import os
from google import genai
from google.genai.errors import ClientError
from dotenv import load_dotenv

load_dotenv()
api_key = os.getenv("GEMINI_API_KEY")

try:
    client = genai.Client(api_key=api_key)
    response = client.models.generate_content(
        model="gemini-2.5-flash",
        contents="Explain how AI works in a few words"
    )
    print("AI Response:")
    print(response.text)
except ClientError as e:
    print(f"ClientError occurred: {e}")
